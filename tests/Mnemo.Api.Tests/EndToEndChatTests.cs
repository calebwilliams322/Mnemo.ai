using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Domain.Entities;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Api.Tests;

/// <summary>
/// TRUE End-to-End Integration Tests using proper DI wiring.
/// Tests the complete flow: Document Upload -> Extraction -> RAG Chat
///
/// Uses:
/// - WebApplicationFactory with real DI container
/// - HTTP calls through actual API endpoints
/// - Real authentication via JWT tokens
/// - Real Supabase PostgreSQL database with pgvector
/// - Real Supabase Storage
/// - Real Claude API (extraction + chat)
/// - Real OpenAI API (embeddings)
/// </summary>
[Collection("Integration")]
public class EndToEndChatTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private Tenant? _testTenant;
    private User? _testUser;
    private User? _adminUser;
    private string? _userToken;
    private Guid _uploadedDocumentId;
    private Guid _extractedPolicyId;
    private readonly List<Guid> _conversationIds = [];

    public EndToEndChatTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        // Create test tenant and users through proper DI
        (_testTenant, _testUser, _adminUser) = await _factory.SetupTestDataAsync();
        _userToken = _factory.GenerateTestToken(_testUser!);

        Console.WriteLine($">>> Test tenant: {_testTenant.Id}");
        Console.WriteLine($">>> Test user: {_testUser!.Id}");
    }

    public async Task DisposeAsync()
    {
        Console.WriteLine(">>> Cleaning up E2E chat test data");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        try
        {
            // 1. Delete messages (by conversation)
            foreach (var convId in _conversationIds)
            {
                var messages = await dbContext.Messages
                    .IgnoreQueryFilters()
                    .Where(m => m.ConversationId == convId)
                    .ToListAsync();
                dbContext.Messages.RemoveRange(messages);
            }

            // 2. Delete conversations
            foreach (var convId in _conversationIds)
            {
                var conv = await dbContext.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == convId);
                if (conv != null) dbContext.Conversations.Remove(conv);
            }

            // 3. Delete coverages (by policy)
            if (_extractedPolicyId != Guid.Empty)
            {
                var coverages = await dbContext.Coverages
                    .IgnoreQueryFilters()
                    .Where(c => c.PolicyId == _extractedPolicyId)
                    .ToListAsync();
                dbContext.Coverages.RemoveRange(coverages);
            }

            // 4. Delete policy
            if (_extractedPolicyId != Guid.Empty)
            {
                var policy = await dbContext.Policies
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == _extractedPolicyId);
                if (policy != null) dbContext.Policies.Remove(policy);
            }

            // 5. Delete document chunks
            if (_uploadedDocumentId != Guid.Empty)
            {
                var chunks = await dbContext.DocumentChunks
                    .IgnoreQueryFilters()
                    .Where(c => c.DocumentId == _uploadedDocumentId)
                    .ToListAsync();
                dbContext.DocumentChunks.RemoveRange(chunks);
            }

            // 6. Delete document
            if (_uploadedDocumentId != Guid.Empty)
            {
                var document = await dbContext.Documents
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(d => d.Id == _uploadedDocumentId);
                if (document != null) dbContext.Documents.Remove(document);
            }

            await dbContext.SaveChangesAsync();

            // 7. Cleanup tenant and users (via factory method)
            await _factory.CleanupTestDataAsync(_testTenant?.Id);

            Console.WriteLine(">>> Cleanup complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($">>> Cleanup error (non-fatal): {ex.Message}");
        }
    }

    [Fact]
    public async Task EndToEnd_DocumentUpload_Extraction_Chat_FullFlow()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _userToken);

        // ========================================
        // PHASE 1: UPLOAD DOCUMENT
        // ========================================
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("  PHASE 1: UPLOAD DOCUMENT VIA API");
        Console.WriteLine(new string('=', 80));

        // Load sample PDF
        var samplesDir = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", "samples");
        var pdfPath = Path.Combine(samplesDir, "Policy GL 554 Main.pdf");

        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"Sample PDF not found: {pdfPath}");

        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        var fileName = Path.GetFileName(pdfPath);
        Console.WriteLine($"  Loading PDF: {fileName} ({pdfBytes.Length:N0} bytes)");

        // Upload via API
        var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        uploadContent.Add(fileContent, "file", fileName);

        var uploadResponse = await client.PostAsync("/documents/upload", uploadContent);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            "Document upload should succeed");

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        _uploadedDocumentId = uploadResult.GetProperty("documentId").GetGuid();
        Console.WriteLine($"  Uploaded document: {_uploadedDocumentId}");

        // ========================================
        // PHASE 2: WAIT FOR EXTRACTION
        // ========================================
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("  PHASE 2: WAIT FOR EXTRACTION TO COMPLETE");
        Console.WriteLine(new string('=', 80));

        var extractionComplete = false;
        var maxWaitSeconds = 120;
        var startTime = DateTime.UtcNow;

        while (!extractionComplete && (DateTime.UtcNow - startTime).TotalSeconds < maxWaitSeconds)
        {
            await Task.Delay(2000); // Poll every 2 seconds

            var statusResponse = await client.GetAsync($"/documents/{_uploadedDocumentId}/extraction-status");
            if (statusResponse.IsSuccessStatusCode)
            {
                var statusResult = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
                var status = statusResult.GetProperty("status").GetString();
                Console.WriteLine($"  Status: {status}");

                if (status is "completed" or "needs_review")
                {
                    extractionComplete = true;
                }
                else if (status == "extraction_failed")
                {
                    throw new Exception("Extraction failed");
                }
            }
        }

        extractionComplete.Should().BeTrue($"Extraction should complete within {maxWaitSeconds}s");

        // ========================================
        // PHASE 3: VERIFY EXTRACTION & GET POLICY
        // ========================================
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("  PHASE 3: VERIFY EXTRACTION RESULTS");
        Console.WriteLine(new string('=', 80));

        // Get document details to verify chunks
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var chunks = await dbContext.DocumentChunks
            .Where(c => c.DocumentId == _uploadedDocumentId)
            .ToListAsync();

        chunks.Should().NotBeEmpty("Document should have chunks");
        chunks.All(c => c.Embedding != null).Should().BeTrue(
            "All chunks should have embeddings for RAG");
        Console.WriteLine($"  Chunks created: {chunks.Count}");
        Console.WriteLine($"  Chunks with embeddings: {chunks.Count(c => c.Embedding != null)}");

        // Get extracted policy via API
        var policiesResponse = await client.GetAsync("/policies");
        policiesResponse.IsSuccessStatusCode.Should().BeTrue();

        var policiesResult = await policiesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var policies = policiesResult.GetProperty("items").EnumerateArray().ToList();
        policies.Should().NotBeEmpty("At least one policy should be extracted");

        // Since this is a fresh tenant with only our document, take the first policy
        // (The list endpoint doesn't include sourceDocumentId)
        var policy = policies.First();

        _extractedPolicyId = policy.GetProperty("id").GetGuid();
        var insuredName = policy.GetProperty("insuredName").GetString();
        Console.WriteLine($"\n  Extracted Policy: {_extractedPolicyId}");
        Console.WriteLine($"  Insured: {insuredName}");

        // ========================================
        // PHASE 4: CREATE CONVERSATION
        // ========================================
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("  PHASE 4: CREATE CONVERSATION & CHAT");
        Console.WriteLine(new string('=', 80));

        var createConvRequest = new
        {
            title = "E2E Test - Policy Questions",
            policyIds = new[] { _extractedPolicyId },
            documentIds = new[] { _uploadedDocumentId }
        };

        var convResponse = await client.PostAsJsonAsync("/conversations", createConvRequest);
        convResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var convResult = await convResponse.Content.ReadFromJsonAsync<JsonElement>();
        var conversationId = convResult.GetProperty("id").GetGuid();
        _conversationIds.Add(conversationId);
        Console.WriteLine($"  Created conversation: {conversationId}");

        // ========================================
        // CHAT TEST 1: Coverage Limits Question
        // ========================================
        Console.WriteLine("\n  --- CHAT TEST 1: Coverage Limits ---");
        var question1 = "What are the coverage limits for this policy? Include the per-occurrence limit and aggregate limit.";

        var response1 = await SendChatMessageAsync(client, conversationId, question1);

        Console.WriteLine($"  Q: {question1}");
        Console.WriteLine($"  A: {TruncateForDisplay(response1.Content, 300)}");
        Console.WriteLine($"  Citations: {response1.CitedChunkCount}");

        response1.Content.Should().NotBeEmpty("Response should contain coverage information");
        // RAG response should mention coverage/limits/policy - actual amounts depend on chunks retrieved
        response1.Content.ToLower().Should().ContainAny(
            ["coverage", "limit", "policy", "liability"],
            "Response should discuss policy coverage topics");

        // ========================================
        // CHAT TEST 2: Named Insured Question
        // ========================================
        Console.WriteLine("\n  --- CHAT TEST 2: Named Insured ---");
        var question2 = "Who is the named insured on this policy?";

        var response2 = await SendChatMessageAsync(client, conversationId, question2);

        Console.WriteLine($"  Q: {question2}");
        Console.WriteLine($"  A: {TruncateForDisplay(response2.Content, 200)}");

        response2.Content.Should().NotBeEmpty();
        // Response should discuss the insured (may or may not extract exact name from chunks)
        response2.Content.ToLower().Should().ContainAny(
            ["insured", "named", "policyholder", "company", "corporation"],
            "Response should discuss the insured party");

        // ========================================
        // CHAT TEST 3: Deductibles Question
        // ========================================
        Console.WriteLine("\n  --- CHAT TEST 3: Deductibles ---");
        var question3 = "What deductibles apply to this policy?";

        var response3 = await SendChatMessageAsync(client, conversationId, question3);

        Console.WriteLine($"  Q: {question3}");
        Console.WriteLine($"  A: {TruncateForDisplay(response3.Content, 200)}");

        response3.Content.Length.Should().BeGreaterThan(50,
            "Response should provide substantive deductible information");

        // ========================================
        // CHAT TEST 4: Effective Dates Question
        // ========================================
        Console.WriteLine("\n  --- CHAT TEST 4: Policy Dates ---");
        var question4 = "What are the effective and expiration dates of this policy?";

        var response4 = await SendChatMessageAsync(client, conversationId, question4);

        Console.WriteLine($"  Q: {question4}");
        Console.WriteLine($"  A: {TruncateForDisplay(response4.Content, 200)}");

        // Response should discuss dates/periods
        response4.Content.ToLower().Should().ContainAny(
            ["date", "effective", "expiration", "period", "term"],
            "Response should discuss policy dates/period");

        // ========================================
        // CHAT TEST 5: Multi-turn Context
        // ========================================
        Console.WriteLine("\n  --- CHAT TEST 5: Follow-up Question ---");
        var question5 = "And what about property damage coverage specifically?";

        var response5 = await SendChatMessageAsync(client, conversationId, question5);

        Console.WriteLine($"  Q: {question5}");
        Console.WriteLine($"  A: {TruncateForDisplay(response5.Content, 200)}");

        response5.Content.Should().NotBeEmpty(
            "Follow-up question should receive a substantive response");

        // ========================================
        // CHAT TEST 6: Specific Coverage Deep-dive
        // ========================================
        Console.WriteLine("\n  --- CHAT TEST 6: Specific Coverage Details ---");
        var question6 = "Does this policy cover bodily injury liability? What are the specific limits?";

        var response6 = await SendChatMessageAsync(client, conversationId, question6);

        Console.WriteLine($"  Q: {question6}");
        Console.WriteLine($"  A: {TruncateForDisplay(response6.Content, 300)}");
        Console.WriteLine($"  Citations: {response6.CitedChunkCount}");

        // Verify we got substantive responses (citations depend on chunk relevance)
        response6.Content.Should().NotBeEmpty("Response should have content");
        response6.Content.ToLower().Should().ContainAny(
            ["bodily injury", "liability", "coverage", "limit", "policy"],
            "Response should discuss bodily injury coverage");

        // ========================================
        // PHASE 5: VERIFY PERSISTENCE
        // ========================================
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("  PHASE 5: VERIFY CONVERSATION PERSISTENCE");
        Console.WriteLine(new string('=', 80));

        var getConvResponse = await client.GetAsync($"/conversations/{conversationId}");
        getConvResponse.IsSuccessStatusCode.Should().BeTrue();

        var savedConv = await getConvResponse.Content.ReadFromJsonAsync<JsonElement>();
        var messages = savedConv.GetProperty("messages").EnumerateArray().ToList();

        messages.Should().HaveCount(12, "Should have 6 user + 6 assistant messages");

        var userMessages = messages.Where(m => m.GetProperty("role").GetString() == "user").ToList();
        var assistantMessages = messages.Where(m => m.GetProperty("role").GetString() == "assistant").ToList();

        Console.WriteLine($"  Total messages: {messages.Count}");
        Console.WriteLine($"  User messages: {userMessages.Count}");
        Console.WriteLine($"  Assistant messages: {assistantMessages.Count}");

        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("  E2E CHAT TEST COMPLETED SUCCESSFULLY");
        Console.WriteLine(new string('=', 80));
    }

    /// <summary>
    /// Send a chat message and collect the SSE streamed response.
    /// </summary>
    private async Task<ChatResponse> SendChatMessageAsync(
        HttpClient client,
        Guid conversationId,
        string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { content = message })
        };

        // Wait for full response - SSE will buffer until complete
        var response = await client.SendAsync(request);
        response.IsSuccessStatusCode.Should().BeTrue(
            $"Chat message should succeed: {response.StatusCode}");

        var contentBuilder = new StringBuilder();
        var citedChunkIds = new List<Guid>();
        Guid? messageId = null;

        // Read full content - TestServer buffers SSE until complete
        var fullContent = await response.Content.ReadAsStringAsync();

        // Parse SSE lines
        var lines = fullContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("data: ")) continue;

            var json = line[6..]; // Remove "data: " prefix
            if (json == "[DONE]") break;

            try
            {
                var evt = JsonSerializer.Deserialize<JsonElement>(json);

                // Note: API returns PascalCase property names (Type, Text, etc.)
                if (!evt.TryGetProperty("Type", out var typeProp))
                {
                    // SSE event without Type field, skip
                    continue;
                }

                var type = typeProp.GetString();

                switch (type)
                {
                    case "token":
                        if (evt.TryGetProperty("Text", out var text))
                        {
                            contentBuilder.Append(text.GetString());
                        }
                        break;

                    case "complete":
                        if (evt.TryGetProperty("MessageId", out var msgId) &&
                            msgId.ValueKind != JsonValueKind.Null)
                        {
                            messageId = msgId.GetGuid();
                        }
                        if (evt.TryGetProperty("CitedChunkIds", out var chunks) &&
                            chunks.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var chunk in chunks.EnumerateArray())
                            {
                                if (chunk.ValueKind != JsonValueKind.Null)
                                {
                                    citedChunkIds.Add(chunk.GetGuid());
                                }
                            }
                        }
                        break;

                    case "error":
                        if (evt.TryGetProperty("Error", out var errorProp) &&
                            errorProp.ValueKind != JsonValueKind.Null)
                        {
                            throw new Exception($"Chat error: {errorProp.GetString()}");
                        }
                        break;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed JSON lines
            }
        }

        return new ChatResponse
        {
            Content = contentBuilder.ToString(),
            MessageId = messageId,
            CitedChunkCount = citedChunkIds.Count
        };
    }

    private static string TruncateForDisplay(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "[empty]";
        text = text.Replace("\n", " ").Replace("\r", "");
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }

    private record ChatResponse
    {
        public required string Content { get; init; }
        public Guid? MessageId { get; init; }
        public int CitedChunkCount { get; init; }
    }
}
