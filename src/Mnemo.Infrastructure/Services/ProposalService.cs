using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using OpenXmlWordprocessing = DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Extraction.Interfaces;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Service for managing proposal templates and generating proposals.
/// Uses RAG + Claude to generate rich proposal content from policy documents.
/// </summary>
public class ProposalService : IProposalService
{
    private readonly MnemoDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly ISemanticSearchService _semanticSearch;
    private readonly IEmbeddingService _embeddingService;
    private readonly IClaudeChatService _claudeChat;
    private readonly ILogger<ProposalService> _logger;

    public ProposalService(
        MnemoDbContext dbContext,
        IStorageService storageService,
        ISemanticSearchService semanticSearch,
        IEmbeddingService embeddingService,
        IClaudeChatService claudeChat,
        ILogger<ProposalService> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _semanticSearch = semanticSearch;
        _embeddingService = embeddingService;
        _claudeChat = claudeChat;
        _logger = logger;
    }

    // ==========================================================================
    // Template Management
    // ==========================================================================

    public async Task<ProposalTemplate> UploadTemplateAsync(
        Stream fileStream,
        string fileName,
        string name,
        string? description,
        Guid tenantId)
    {
        var templateId = Guid.NewGuid();

        // Upload to storage
        var storagePath = await _storageService.UploadAsync(
            tenantId,
            templateId,
            $"{templateId}.docx",
            fileStream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        // Create database record
        // Note: We don't extract placeholders anymore since we use RAG + Claude for content generation
        var template = new ProposalTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = name,
            Description = description,
            StoragePath = storagePath,
            OriginalFileName = fileName,
            FileSizeBytes = fileStream.Length,
            Placeholders = "[]", // Not used with RAG approach
            IsActive = true,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProposalTemplates.Add(template);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Created proposal template {TemplateId} for tenant {TenantId}",
            templateId, tenantId);

        return template;
    }

    public async Task<List<ProposalTemplate>> GetTemplatesAsync(Guid tenantId)
    {
        // Ensure default template exists (seeds on first access)
        await EnsureDefaultTemplateAsync(tenantId);

        return await _dbContext.ProposalTemplates
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderByDescending(t => t.IsDefault)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<ProposalTemplate?> GetTemplateAsync(Guid id, Guid tenantId)
    {
        return await _dbContext.ProposalTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId);
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, Guid tenantId)
    {
        var template = await _dbContext.ProposalTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId);

        if (template == null)
            return false;

        // Soft delete
        template.IsActive = false;
        template.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Soft-deleted proposal template {TemplateId}", id);
        return true;
    }

    // ==========================================================================
    // Proposal Generation
    // ==========================================================================

    public async Task<Proposal> GenerateProposalAsync(
        Guid templateId,
        List<Guid> policyIds,
        Guid tenantId,
        Guid? userId = null)
    {
        // Get template (we still use it for metadata, but content comes from Claude)
        var template = await _dbContext.ProposalTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId && t.IsActive);

        if (template == null)
            throw new ArgumentException($"Template {templateId} not found");

        // Get policies with basic info
        var policies = await _dbContext.Policies
            .Where(p => policyIds.Contains(p.Id) && p.TenantId == tenantId)
            .ToListAsync();

        if (policies.Count == 0)
            throw new ArgumentException("No valid policies found");

        // Create proposal record
        var proposalId = Guid.NewGuid();
        var clientName = policies.First().InsuredName ?? "Unknown Client";

        var proposal = new Proposal
        {
            Id = proposalId,
            TenantId = tenantId,
            TemplateId = templateId,
            ClientName = clientName,
            PolicyIds = JsonSerializer.Serialize(policyIds),
            Status = "processing",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _dbContext.Proposals.Add(proposal);
        await _dbContext.SaveChangesAsync();

        try
        {
            // Step 1: Get document chunks via RAG for all selected policies
            _logger.LogInformation("Fetching RAG context for {PolicyCount} policies", policyIds.Count);

            var queryText = "coverage summary limits deductibles premiums declarations schedule";
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(queryText);

            if (!embeddingResult.Success || embeddingResult.Embeddings.Count == 0)
                throw new InvalidOperationException("Failed to generate embedding for proposal query");

            var searchRequest = new SemanticSearchRequest
            {
                QueryEmbedding = embeddingResult.Embeddings[0],
                TenantId = tenantId,
                PolicyIds = policyIds,
                TopK = 15, // Get plenty of context per policy
                MinSimilarity = 0.5,
                BalancedRetrieval = true,
                ChunksPerPolicy = 15
            };

            var chunks = await _semanticSearch.SearchAsync(searchRequest);
            _logger.LogInformation("Retrieved {ChunkCount} document chunks for proposal", chunks.Count);

            // Step 2: Build prompt for Claude to generate proposal content
            var prompt = BuildProposalPrompt(policies, chunks, clientName);

            // Step 3: Call Claude to generate the proposal content
            var chatRequest = new ChatRequest
            {
                SystemPrompt = ProposalSystemPrompt,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = prompt }
                },
                MaxTokens = 4096
            };

            var responseBuilder = new StringBuilder();
            await foreach (var streamEvent in _claudeChat.StreamChatAsync(chatRequest))
            {
                if (!string.IsNullOrEmpty(streamEvent.Text))
                {
                    responseBuilder.Append(streamEvent.Text);
                }
            }

            var proposalContent = responseBuilder.ToString();
            _logger.LogInformation("Claude generated proposal content: {Length} chars", proposalContent.Length);

            // Step 4: Create Word document with the generated content
            var documentStream = CreateProposalDocument(clientName, proposalContent);

            // Step 5: Upload generated document
            var outputPath = await _storageService.UploadAsync(
                tenantId,
                proposalId,
                $"{proposalId}.docx",
                documentStream,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

            // Update proposal record
            proposal.OutputStoragePath = outputPath;
            proposal.Status = "completed";
            proposal.GeneratedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Generated proposal {ProposalId} for {PolicyCount} policies using RAG + Claude",
                proposalId, policies.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate proposal {ProposalId}", proposalId);
            proposal.Status = "failed";
            proposal.ErrorMessage = ex.Message;
            await _dbContext.SaveChangesAsync();
            throw;
        }

        return proposal;
    }

    private const string ProposalSystemPrompt = """
        You are an expert insurance proposal writer. Generate professional proposals using this EXACT format.

        ## OUTPUT FORMAT (Follow exactly - this will be parsed programmatically)

        [HEADER]
        prepared_for: {client name}
        prepared_by: {agency name if found, otherwise "Your Insurance Agency"}
        policy_types: {comma-separated list of coverage types}
        policy_period: {effective date} - {expiration date}
        carriers: {comma-separated list of carrier names}
        [/HEADER]

        [INSURED_INFO]
        named_insured: {full legal name}
        business_type: {type of business if found}
        mailing_address: {full address}
        [/INSURED_INFO]

        [COVERAGE_SECTION]
        section_title: {Coverage Type} Insurance Overview (e.g., "Commercial Property Insurance Overview")

        [TABLE]
        header: Coverage | Limit | Deductible | Premium
        row: {coverage name} | {limit amount} | {deductible} | {premium if known}
        row: {next coverage} | {limit} | {deductible} | {premium}
        [/TABLE]

        notes: {any important notes about this coverage section}
        [/COVERAGE_SECTION]

        (Repeat [COVERAGE_SECTION] for each type of coverage: Property, General Liability, Auto, Workers Comp, Umbrella, etc.)

        [PREMIUM_SUMMARY]
        [TABLE]
        header: Coverage | Premium
        row: {coverage type} | {premium amount}
        row: {coverage type} | {premium amount}
        row: Taxes & Fees | {amount if applicable}
        row: **Total** | **{total premium}**
        [/TABLE]
        [/PREMIUM_SUMMARY]

        ## RULES
        - Extract ALL coverages, limits, and deductibles from the documents
        - Format currency as $X,XXX.XX
        - Use "Not Specified" if a value isn't found
        - Be thorough and accurate - these are legal documents
        - Include every coverage you find, no matter how minor
        - Group coverages logically by type (Property, Liability, Auto, etc.)
        """;

    private string BuildProposalPrompt(List<Policy> policies, List<ChunkSearchResult> chunks, string clientName)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Please generate an insurance proposal for: **{clientName}**");
        sb.AppendLine($"Date: {DateTime.Now:MMMM d, yyyy}");
        sb.AppendLine();
        sb.AppendLine("## Policies to Include:");
        sb.AppendLine();

        foreach (var policy in policies)
        {
            sb.AppendLine($"### {policy.CarrierName ?? "Unknown Carrier"}");
            sb.AppendLine($"- Policy/Quote Number: {policy.PolicyNumber ?? policy.QuoteNumber ?? "N/A"}");
            sb.AppendLine($"- Term: {policy.EffectiveDate?.ToString("MM/dd/yyyy") ?? "N/A"} to {policy.ExpirationDate?.ToString("MM/dd/yyyy") ?? "N/A"}");
            if (policy.TotalPremium.HasValue)
                sb.AppendLine($"- Total Premium: ${policy.TotalPremium:N2}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Policy Document Excerpts:");
        sb.AppendLine();
        sb.AppendLine("Use the following document excerpts to extract coverage details, limits, and deductibles:");
        sb.AppendLine();

        // Group chunks by policy for clarity
        var groupedChunks = chunks
            .GroupBy(c => c.PolicyId ?? Guid.Empty)
            .OrderBy(g => g.Key);

        foreach (var group in groupedChunks)
        {
            var firstChunk = group.First();
            var policyLabel = !string.IsNullOrEmpty(firstChunk.CarrierName)
                ? firstChunk.CarrierName
                : "Policy";

            sb.AppendLine($"### {policyLabel} Documents:");
            sb.AppendLine();

            foreach (var chunk in group)
            {
                sb.AppendLine("---");
                sb.Append($"[{chunk.DocumentName}");
                if (chunk.PageStart.HasValue)
                    sb.Append($", Page {chunk.PageStart}");
                sb.AppendLine("]");
                sb.AppendLine(chunk.ChunkText);
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Please generate a professional proposal document with complete coverage tables for each policy.");

        return sb.ToString();
    }

    private Stream CreateProposalDocument(string clientName, string proposalContent)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new OpenXmlWordprocessing.Document();
            var body = mainPart.Document.AppendChild(new OpenXmlWordprocessing.Body());

            // Parse Claude's structured output
            var parsedContent = ParseProposalContent(proposalContent);

            // === TITLE SECTION ===
            AddStyledParagraph(body, "Insurance Overview", fontSize: 36, bold: true);
            AddHorizontalRule(body);

            // === HEADER INFO ===
            AddKeyValueLine(body, "Prepared For:", parsedContent.GetValueOrDefault("prepared_for", clientName));
            AddKeyValueLine(body, "Prepared By:", parsedContent.GetValueOrDefault("prepared_by", "Your Insurance Agency"));
            AddKeyValueLine(body, "Policy Type:", parsedContent.GetValueOrDefault("policy_types", "Commercial Insurance"));
            AddKeyValueLine(body, "Policy Period:", parsedContent.GetValueOrDefault("policy_period", "See policy documents"));
            AddKeyValueLine(body, "Agent Contact:", parsedContent.GetValueOrDefault("agent_contact", "Contact your agent"));
            AddKeyValueLine(body, "Carriers:", parsedContent.GetValueOrDefault("carriers", "See policy documents"));

            AddHorizontalRule(body);

            // === NAMED INSURED INFO ===
            AddStyledParagraph(body, "Named Insured Information", fontSize: 28, bold: true);
            AddBulletPoint(body, "Named Insured:", parsedContent.GetValueOrDefault("named_insured", clientName));
            if (parsedContent.ContainsKey("business_type"))
                AddBulletPoint(body, "Business Type:", parsedContent["business_type"]);
            if (parsedContent.ContainsKey("mailing_address"))
                AddBulletPoint(body, "Mailing Address:", parsedContent["mailing_address"]);

            AddHorizontalRule(body);

            // === CLAIMS REPORTING ===
            AddStyledParagraph(body, "Claims Reporting", fontSize: 28, bold: true);
            AddStyledParagraph(body, "To report a claim, contact:");
            AddBulletPoint(body, "Your Agent:", parsedContent.GetValueOrDefault("agent_phone", "(Contact your insurance agency)"));

            AddHorizontalRule(body);
            AddStyledParagraph(body, ""); // Spacing

            // === COVERAGE SECTIONS WITH TABLES ===
            var coverageSections = ParseCoverageSections(proposalContent);
            foreach (var section in coverageSections)
            {
                AddStyledParagraph(body, section.Title, fontSize: 28, bold: true);

                if (section.TableData.Count > 0)
                {
                    AddTable(body, section.TableData, section.Headers);
                }

                if (!string.IsNullOrEmpty(section.Notes))
                {
                    AddStyledParagraph(body, "");
                    AddStyledParagraph(body, section.Notes, italic: true);
                }

                AddStyledParagraph(body, ""); // Spacing between sections
            }

            // === PREMIUM SUMMARY ===
            var premiumSection = ParsePremiumSummary(proposalContent);
            if (premiumSection.TableData.Count > 0)
            {
                AddHorizontalRule(body);
                AddStyledParagraph(body, "Premium Summary", fontSize: 28, bold: true);
                AddTable(body, premiumSection.TableData, premiumSection.Headers, highlightLastRow: true);
            }

            // === FOOTER ===
            AddStyledParagraph(body, "");
            AddHorizontalRule(body);
            AddStyledParagraph(body, "");
            AddStyledParagraph(body, "Please review this proposal and contact us with any questions.", italic: true);
            AddStyledParagraph(body, $"Generated: {DateTime.Now:MMMM d, yyyy}", italic: true, fontSize: 20);

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private Dictionary<string, string> ParseProposalContent(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Parse [HEADER] section
        var headerMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"\[HEADER\](.*?)\[/HEADER\]", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (headerMatch.Success)
        {
            ParseKeyValues(headerMatch.Groups[1].Value, result);
        }

        // Parse [INSURED_INFO] section
        var insuredMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"\[INSURED_INFO\](.*?)\[/INSURED_INFO\]", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (insuredMatch.Success)
        {
            ParseKeyValues(insuredMatch.Groups[1].Value, result);
        }

        return result;
    }

    private void ParseKeyValues(string section, Dictionary<string, string> result)
    {
        var lines = section.Split('\n');
        foreach (var line in lines)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    result[key] = value;
                }
            }
        }
    }

    private class CoverageSection
    {
        public string Title { get; set; } = "";
        public List<string> Headers { get; set; } = new();
        public List<List<string>> TableData { get; set; } = new();
        public string? Notes { get; set; }
    }

    private List<CoverageSection> ParseCoverageSections(string content)
    {
        var sections = new List<CoverageSection>();

        var sectionMatches = System.Text.RegularExpressions.Regex.Matches(
            content, @"\[COVERAGE_SECTION\](.*?)\[/COVERAGE_SECTION\]",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in sectionMatches)
        {
            var sectionContent = match.Groups[1].Value;
            var section = new CoverageSection();

            // Parse title
            var titleMatch = System.Text.RegularExpressions.Regex.Match(sectionContent, @"section_title:\s*(.+)");
            if (titleMatch.Success)
            {
                section.Title = titleMatch.Groups[1].Value.Trim();
            }

            // Parse table
            var tableMatch = System.Text.RegularExpressions.Regex.Match(
                sectionContent, @"\[TABLE\](.*?)\[/TABLE\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (tableMatch.Success)
            {
                ParseTable(tableMatch.Groups[1].Value, section);
            }

            // Parse notes
            var notesMatch = System.Text.RegularExpressions.Regex.Match(sectionContent, @"notes:\s*(.+)");
            if (notesMatch.Success)
            {
                section.Notes = notesMatch.Groups[1].Value.Trim();
            }

            if (!string.IsNullOrEmpty(section.Title))
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    private CoverageSection ParsePremiumSummary(string content)
    {
        var section = new CoverageSection { Title = "Premium Summary" };

        var summaryMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"\[PREMIUM_SUMMARY\](.*?)\[/PREMIUM_SUMMARY\]",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (summaryMatch.Success)
        {
            var tableMatch = System.Text.RegularExpressions.Regex.Match(
                summaryMatch.Groups[1].Value, @"\[TABLE\](.*?)\[/TABLE\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (tableMatch.Success)
            {
                ParseTable(tableMatch.Groups[1].Value, section);
            }
        }

        return section;
    }

    private void ParseTable(string tableContent, CoverageSection section)
    {
        var lines = tableContent.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("header:"))
            {
                var headerPart = trimmed.Substring(7).Trim();
                section.Headers = headerPart.Split('|').Select(h => h.Trim()).ToList();
            }
            else if (trimmed.StartsWith("row:"))
            {
                var rowPart = trimmed.Substring(4).Trim();
                var cells = rowPart.Split('|').Select(c => c.Trim().Replace("**", "")).ToList();
                section.TableData.Add(cells);
            }
        }
    }

    // ==========================================================================
    // Word Document Styling Helpers
    // ==========================================================================

    private void AddStyledParagraph(OpenXmlWordprocessing.Body body, string text, int fontSize = 24, bool bold = false, bool italic = false)
    {
        var para = body.AppendChild(new OpenXmlWordprocessing.Paragraph());
        var pProps = para.AppendChild(new OpenXmlWordprocessing.ParagraphProperties());
        pProps.AppendChild(new OpenXmlWordprocessing.SpacingBetweenLines { After = "120", Line = "276", LineRule = OpenXmlWordprocessing.LineSpacingRuleValues.Auto });

        var run = para.AppendChild(new OpenXmlWordprocessing.Run());
        var rProps = run.AppendChild(new OpenXmlWordprocessing.RunProperties());

        // Use Aptos/Calibri Light (majorHAnsi theme)
        rProps.AppendChild(new OpenXmlWordprocessing.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" });
        rProps.AppendChild(new OpenXmlWordprocessing.FontSize { Val = fontSize.ToString() });

        if (bold) rProps.AppendChild(new OpenXmlWordprocessing.Bold());
        if (italic) rProps.AppendChild(new OpenXmlWordprocessing.Italic());

        run.AppendChild(new OpenXmlWordprocessing.Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private void AddKeyValueLine(OpenXmlWordprocessing.Body body, string key, string value)
    {
        var para = body.AppendChild(new OpenXmlWordprocessing.Paragraph());
        var pProps = para.AppendChild(new OpenXmlWordprocessing.ParagraphProperties());
        pProps.AppendChild(new OpenXmlWordprocessing.SpacingBetweenLines { After = "40", Line = "276", LineRule = OpenXmlWordprocessing.LineSpacingRuleValues.Auto });

        // Bold key
        var keyRun = para.AppendChild(new OpenXmlWordprocessing.Run());
        var keyProps = keyRun.AppendChild(new OpenXmlWordprocessing.RunProperties());
        keyProps.AppendChild(new OpenXmlWordprocessing.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" });
        keyProps.AppendChild(new OpenXmlWordprocessing.FontSize { Val = "24" });
        keyProps.AppendChild(new OpenXmlWordprocessing.Bold());
        keyRun.AppendChild(new OpenXmlWordprocessing.Text(key + " ") { Space = SpaceProcessingModeValues.Preserve });

        // Regular value
        var valueRun = para.AppendChild(new OpenXmlWordprocessing.Run());
        var valueProps = valueRun.AppendChild(new OpenXmlWordprocessing.RunProperties());
        valueProps.AppendChild(new OpenXmlWordprocessing.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" });
        valueProps.AppendChild(new OpenXmlWordprocessing.FontSize { Val = "24" });
        valueRun.AppendChild(new OpenXmlWordprocessing.Text(value) { Space = SpaceProcessingModeValues.Preserve });
    }

    private void AddBulletPoint(OpenXmlWordprocessing.Body body, string label, string value)
    {
        var para = body.AppendChild(new OpenXmlWordprocessing.Paragraph());
        var pProps = para.AppendChild(new OpenXmlWordprocessing.ParagraphProperties());
        pProps.AppendChild(new OpenXmlWordprocessing.SpacingBetweenLines { After = "40" });
        pProps.AppendChild(new OpenXmlWordprocessing.Indentation { Left = "360" }); // Indent for bullet effect

        // Bullet character
        var bulletRun = para.AppendChild(new OpenXmlWordprocessing.Run());
        var bulletProps = bulletRun.AppendChild(new OpenXmlWordprocessing.RunProperties());
        bulletProps.AppendChild(new OpenXmlWordprocessing.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" });
        bulletProps.AppendChild(new OpenXmlWordprocessing.FontSize { Val = "24" });
        bulletRun.AppendChild(new OpenXmlWordprocessing.Text("• ") { Space = SpaceProcessingModeValues.Preserve });

        // Bold label
        var labelRun = para.AppendChild(new OpenXmlWordprocessing.Run());
        var labelProps = labelRun.AppendChild(new OpenXmlWordprocessing.RunProperties());
        labelProps.AppendChild(new OpenXmlWordprocessing.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" });
        labelProps.AppendChild(new OpenXmlWordprocessing.FontSize { Val = "24" });
        labelProps.AppendChild(new OpenXmlWordprocessing.Bold());
        labelRun.AppendChild(new OpenXmlWordprocessing.Text(label + " ") { Space = SpaceProcessingModeValues.Preserve });

        // Value
        var valueRun = para.AppendChild(new OpenXmlWordprocessing.Run());
        var valueProps = valueRun.AppendChild(new OpenXmlWordprocessing.RunProperties());
        valueProps.AppendChild(new OpenXmlWordprocessing.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" });
        valueProps.AppendChild(new OpenXmlWordprocessing.FontSize { Val = "24" });
        valueRun.AppendChild(new OpenXmlWordprocessing.Text(value) { Space = SpaceProcessingModeValues.Preserve });
    }

    private void AddHorizontalRule(OpenXmlWordprocessing.Body body)
    {
        var para = body.AppendChild(new OpenXmlWordprocessing.Paragraph());
        var pProps = para.AppendChild(new OpenXmlWordprocessing.ParagraphProperties());

        // Add bottom border to create horizontal rule effect
        var pBorders = pProps.AppendChild(new OpenXmlWordprocessing.ParagraphBorders());
        pBorders.AppendChild(new OpenXmlWordprocessing.BottomBorder
        {
            Val = OpenXmlWordprocessing.BorderValues.Single,
            Size = 6,
            Color = "A0A0A0",
            Space = 1
        });

        pProps.AppendChild(new OpenXmlWordprocessing.SpacingBetweenLines { After = "120", Before = "120" });
    }

    private void AddTable(OpenXmlWordprocessing.Body body, List<List<string>> rows, List<string> headers, bool highlightLastRow = false)
    {
        if (rows.Count == 0) return;

        var table = body.AppendChild(new OpenXmlWordprocessing.Table());
        var columnCount = headers.Count > 0 ? headers.Count : (rows.Count > 0 ? rows[0].Count : 0);
        if (columnCount == 0) return;

        // Table properties - full width with borders
        var tblProps = table.AppendChild(new OpenXmlWordprocessing.TableProperties());
        tblProps.AppendChild(new OpenXmlWordprocessing.TableWidth { Width = "5000", Type = OpenXmlWordprocessing.TableWidthUnitValues.Pct }); // 100% width
        tblProps.AppendChild(new OpenXmlWordprocessing.TableLayout { Type = OpenXmlWordprocessing.TableLayoutValues.Fixed });

        var tblBorders = tblProps.AppendChild(new OpenXmlWordprocessing.TableBorders());
        tblBorders.AppendChild(new OpenXmlWordprocessing.TopBorder { Val = OpenXmlWordprocessing.BorderValues.Single, Size = 4, Color = "808080" });
        tblBorders.AppendChild(new OpenXmlWordprocessing.BottomBorder { Val = OpenXmlWordprocessing.BorderValues.Single, Size = 4, Color = "808080" });
        tblBorders.AppendChild(new OpenXmlWordprocessing.LeftBorder { Val = OpenXmlWordprocessing.BorderValues.Single, Size = 4, Color = "808080" });
        tblBorders.AppendChild(new OpenXmlWordprocessing.RightBorder { Val = OpenXmlWordprocessing.BorderValues.Single, Size = 4, Color = "808080" });
        tblBorders.AppendChild(new OpenXmlWordprocessing.InsideHorizontalBorder { Val = OpenXmlWordprocessing.BorderValues.Single, Size = 4, Color = "D0D0D0" });
        tblBorders.AppendChild(new OpenXmlWordprocessing.InsideVerticalBorder { Val = OpenXmlWordprocessing.BorderValues.Single, Size = 4, Color = "D0D0D0" });

        // Add table grid with equal column widths (9360 twips = 6.5 inches page width, divide by columns)
        var grid = table.AppendChild(new OpenXmlWordprocessing.TableGrid());
        var colWidth = 9360 / columnCount;
        for (int i = 0; i < columnCount; i++)
        {
            grid.AppendChild(new OpenXmlWordprocessing.GridColumn { Width = colWidth.ToString() });
        }

        // Header row
        if (headers.Count > 0)
        {
            var headerRow = table.AppendChild(new OpenXmlWordprocessing.TableRow());
            for (int i = 0; i < headers.Count; i++)
            {
                AddTableCell(headerRow, headers[i], colWidth, isHeader: true);
            }
        }

        // Data rows
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var isLastRow = highlightLastRow && i == rows.Count - 1;
            var tableRow = table.AppendChild(new OpenXmlWordprocessing.TableRow());

            for (int j = 0; j < row.Count; j++)
            {
                AddTableCell(tableRow, row[j], colWidth, isHeader: false, isBold: isLastRow);
            }
        }

        // Add spacing after table
        AddStyledParagraph(body, "");
    }

    private void AddTableCell(OpenXmlWordprocessing.TableRow row, string text, int colWidth, bool isHeader = false, bool isBold = false)
    {
        var cell = row.AppendChild(new OpenXmlWordprocessing.TableCell());

        // Cell properties
        var cellProps = cell.AppendChild(new OpenXmlWordprocessing.TableCellProperties());
        cellProps.AppendChild(new OpenXmlWordprocessing.TableCellWidth { Width = colWidth.ToString(), Type = OpenXmlWordprocessing.TableWidthUnitValues.Dxa });
        cellProps.AppendChild(new OpenXmlWordprocessing.TableCellVerticalAlignment { Val = OpenXmlWordprocessing.TableVerticalAlignmentValues.Center });

        // Shading for header
        if (isHeader)
        {
            cellProps.AppendChild(new OpenXmlWordprocessing.Shading { Fill = "2F5496", Val = OpenXmlWordprocessing.ShadingPatternValues.Clear }); // Blue header
        }

        // Cell margins/padding
        var margins = cellProps.AppendChild(new OpenXmlWordprocessing.TableCellMargin());
        margins.AppendChild(new OpenXmlWordprocessing.TopMargin { Width = "60", Type = OpenXmlWordprocessing.TableWidthUnitValues.Dxa });
        margins.AppendChild(new OpenXmlWordprocessing.BottomMargin { Width = "60", Type = OpenXmlWordprocessing.TableWidthUnitValues.Dxa });
        margins.AppendChild(new OpenXmlWordprocessing.LeftMargin { Width = "100", Type = OpenXmlWordprocessing.TableWidthUnitValues.Dxa });
        margins.AppendChild(new OpenXmlWordprocessing.RightMargin { Width = "100", Type = OpenXmlWordprocessing.TableWidthUnitValues.Dxa });

        // Paragraph and text
        var para = cell.AppendChild(new OpenXmlWordprocessing.Paragraph());
        var run = para.AppendChild(new OpenXmlWordprocessing.Run());
        var rProps = run.AppendChild(new OpenXmlWordprocessing.RunProperties());

        rProps.AppendChild(new OpenXmlWordprocessing.RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" });
        rProps.AppendChild(new OpenXmlWordprocessing.FontSize { Val = isHeader ? "22" : "20" });

        if (isHeader)
        {
            rProps.AppendChild(new OpenXmlWordprocessing.Bold());
            rProps.AppendChild(new OpenXmlWordprocessing.Color { Val = "FFFFFF" }); // White text on blue
        }
        else if (isBold)
        {
            rProps.AppendChild(new OpenXmlWordprocessing.Bold());
        }

        run.AppendChild(new OpenXmlWordprocessing.Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    public async Task<List<Proposal>> GetProposalsAsync(Guid tenantId)
    {
        return await _dbContext.Proposals
            .Include(p => p.Template)
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Stream> DownloadProposalAsync(Guid proposalId, Guid tenantId)
    {
        var proposal = await _dbContext.Proposals
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.TenantId == tenantId);

        if (proposal == null)
            throw new ArgumentException($"Proposal {proposalId} not found");

        if (proposal.Status != "completed" || string.IsNullOrEmpty(proposal.OutputStoragePath))
            throw new InvalidOperationException($"Proposal {proposalId} is not ready for download");

        return await _storageService.DownloadAsync(proposal.OutputStoragePath);
    }

    // ==========================================================================
    // Private Helpers
    // ==========================================================================

    private async Task EnsureDefaultTemplateAsync(Guid tenantId)
    {
        // Check if tenant already has templates
        var hasTemplates = await _dbContext.ProposalTemplates
            .AnyAsync(t => t.TenantId == tenantId);

        if (hasTemplates)
            return;

        // Create default template
        // For now, we'll create a simple placeholder template
        // In production, this would load from an embedded resource
        var defaultTemplate = CreateDefaultTemplateDocument();

        using var templateStream = new MemoryStream(defaultTemplate);
        await UploadTemplateAsync(
            templateStream,
            "default-proposal-template.docx",
            "Default Proposal Template",
            "A professional insurance proposal template with policy and coverage summaries.",
            tenantId);

        // Mark it as default
        var template = await _dbContext.ProposalTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId);
        if (template != null)
        {
            template.IsDefault = true;
            await _dbContext.SaveChangesAsync();
        }

        _logger.LogInformation("Created default proposal template for tenant {TenantId}", tenantId);
    }

    private byte[] CreateDefaultTemplateDocument()
    {
        // Create a simple default template document
        // Note: With RAG + Claude approach, the template is mainly for metadata
        // The actual proposal content is generated dynamically by Claude
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new OpenXmlWordprocessing.Document();
            var body = mainPart.Document.AppendChild(new OpenXmlWordprocessing.Body());

            // Just a placeholder document - actual proposals use Claude-generated content
            AddStyledParagraph(body, "Insurance Overview", fontSize: 36, bold: true);
            AddHorizontalRule(body);
            AddStyledParagraph(body, "This is the default proposal template.");
            AddStyledParagraph(body, "Proposal content will be generated based on your policy documents.");

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
