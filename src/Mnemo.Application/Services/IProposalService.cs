using Mnemo.Domain.Entities;

namespace Mnemo.Application.Services;

/// <summary>
/// Service for managing proposal templates and generating proposals.
/// </summary>
public interface IProposalService
{
    // ==========================================================================
    // Template Management
    // ==========================================================================

    /// <summary>
    /// Uploads a new proposal template.
    /// </summary>
    Task<ProposalTemplate> UploadTemplateAsync(
        Stream fileStream,
        string fileName,
        string name,
        string? description,
        Guid tenantId);

    /// <summary>
    /// Gets all templates for a tenant.
    /// Automatically seeds default template if none exist.
    /// </summary>
    Task<List<ProposalTemplate>> GetTemplatesAsync(Guid tenantId);

    /// <summary>
    /// Gets a specific template by ID.
    /// </summary>
    Task<ProposalTemplate?> GetTemplateAsync(Guid id, Guid tenantId);

    /// <summary>
    /// Soft-deletes a template (marks as inactive).
    /// </summary>
    Task<bool> DeleteTemplateAsync(Guid id, Guid tenantId);

    // ==========================================================================
    // Proposal Generation
    // ==========================================================================

    /// <summary>
    /// Generates a proposal from a template and selected policies.
    /// </summary>
    Task<Proposal> GenerateProposalAsync(
        Guid templateId,
        List<Guid> policyIds,
        Guid tenantId,
        Guid? userId = null);

    /// <summary>
    /// Gets all proposals for a tenant.
    /// </summary>
    Task<List<Proposal>> GetProposalsAsync(Guid tenantId);

    /// <summary>
    /// Gets the generated proposal document as a stream.
    /// </summary>
    Task<Stream> DownloadProposalAsync(Guid proposalId, Guid tenantId);
}
