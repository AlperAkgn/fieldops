using FieldOps.Application.Common;
using FieldOps.Application.Operations;
using FieldOps.Application.WorkOrderDetails;
using FieldOps.Domain.WorkOrderDetails;
using FieldOps.Infrastructure.Operations;
using FieldOps.Infrastructure.Persistence;
using FieldOps.Infrastructure.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.WorkOrderDetails;

internal sealed class ChecklistTemplateService(FieldOpsDbContext database, OperationalWriteContext writes) : IChecklistTemplateService
{
    public async Task<PageResult<ChecklistTemplateSummary>> ListAsync(PageRequest page, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        page.Validate();
        if (search is not null && (search.Length > 120 || search.Any(char.IsControl)))
            throw DetailRules.Invalid("invalid_search", "Search must contain at most 120 characters without control characters.");

        var query = database.ChecklistTemplates.AsNoTracking().Where(template => template.TenantId == actor.TenantId);
        var active = isActive ?? true;
        query = query.Where(template => template.IsActive == active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var literal = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(template => EF.Functions.ILike(template.Name, "%" + literal + "%", "\\"));
        }

        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(template => template.Name).ThenBy(template => template.Id)
            .Skip(page.Offset).Take(page.PageSize)
            .Select(template => new ChecklistTemplateSummary(template.Id, template.Name, template.IsActive,
                template.Version, template.CreatedAt, template.UpdatedAt)).ToListAsync(cancellationToken);
        return new PageResult<ChecklistTemplateSummary>(items, page.Page, page.PageSize, count);
    }

    public async Task<ChecklistTemplateDto> GetAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        return await database.ChecklistTemplates.AsNoTracking()
            .Where(template => template.TenantId == actor.TenantId && template.Id == templateId)
            .Select(template => new ChecklistTemplateDto(template.Id, template.Name, template.IsActive, template.Version,
                template.CreatedAt, template.UpdatedAt,
                database.ChecklistTemplateItems.Where(item => item.TenantId == template.TenantId &&
                    item.TemplateId == template.Id && item.IsActive).OrderBy(item => item.Position).ThenBy(item => item.Id)
                    .Select(item => new ChecklistTemplateItemDto(item.Id, item.Label, item.Position, item.IsRequired)).ToList()))
            .SingleOrDefaultAsync(cancellationToken) ?? throw MissingTemplate();
    }

    public async Task<ChecklistTemplateDto> CreateAsync(CreateChecklistTemplateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        var template = DetailRules.Apply(() => ChecklistTemplate.Create(actor.TenantId, request.Name, writes.Now));
        database.ChecklistTemplates.Add(template);
        writes.Audit("checklist_template", template.Id, "created");
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChecklistTemplateDto(template.Id, template.Name, template.IsActive, template.Version,
            template.CreatedAt, template.UpdatedAt, []);
    }

    public async Task<ChecklistTemplateDto> UpdateAsync(Guid templateId, UpdateChecklistTemplateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var template = await LoadAsync(templateId, cancellationToken);
        DetailRules.Apply(() => template.Rename(request.Name, request.ExpectedVersion, writes.Now));
        writes.Audit("checklist_template", template.Id, "updated");
        await database.SaveChangesAsync(cancellationToken);
        var result = await GetAsync(templateId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task ArchiveAsync(Guid templateId, long expectedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var template = await LoadAsync(templateId, cancellationToken);
        DetailRules.Apply(() => template.Archive(expectedVersion, writes.Now));
        writes.Audit("checklist_template", template.Id, "archived");
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ChecklistTemplateDto> AddItemAsync(Guid templateId, CreateChecklistTemplateItemRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var template = await LoadAsync(templateId, cancellationToken);
        DetailRules.Apply(() => template.Touch(request.ExpectedVersion, writes.Now));
        if (await ActiveItems(template).CountAsync(cancellationToken) >= ChecklistTemplate.MaximumItems)
            throw DetailRules.Conflict("checklist_template_full", $"A checklist template supports at most {ChecklistTemplate.MaximumItems} active items.");
        database.ChecklistTemplateItems.Add(DetailRules.Apply(() =>
            ChecklistTemplateItem.Create(template, request.Label, request.Position, request.IsRequired)));
        writes.Audit("checklist_template", template.Id, "item_added");
        await database.SaveChangesAsync(cancellationToken);
        var result = await GetAsync(templateId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ChecklistTemplateDto> UpdateItemAsync(Guid templateId, Guid itemId, UpdateChecklistTemplateItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Label is null && request.Position is null && request.IsRequired is null)
            throw DetailRules.Invalid("checklist_item_update_empty", "Provide at least one checklist item field to update.");

        await using var transaction = await writes.BeginAsync(cancellationToken);
        var template = await LoadAsync(templateId, cancellationToken);
        var item = await LoadItemAsync(template, itemId, cancellationToken);
        DetailRules.Apply(() => template.Touch(request.ExpectedVersion, writes.Now));
        DetailRules.Apply(() => item.Update(request.Label ?? item.Label, request.Position ?? item.Position, request.IsRequired ?? item.IsRequired));
        writes.Audit("checklist_template", template.Id, "item_updated");
        await database.SaveChangesAsync(cancellationToken);
        var result = await GetAsync(templateId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ChecklistTemplateDto> ArchiveItemAsync(Guid templateId, Guid itemId, long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var transaction = await writes.BeginAsync(cancellationToken);
        var template = await LoadAsync(templateId, cancellationToken);
        var item = await LoadItemAsync(template, itemId, cancellationToken);
        DetailRules.Apply(() => template.Touch(expectedVersion, writes.Now));
        item.Archive();
        writes.Audit("checklist_template", template.Id, "item_archived");
        await database.SaveChangesAsync(cancellationToken);
        var result = await GetAsync(templateId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<ChecklistTemplate> LoadAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var actor = writes.Actor;
        WorkOrderAccess.RequireManager(actor);
        return await database.ChecklistTemplates.SingleOrDefaultAsync(template =>
            template.TenantId == actor.TenantId && template.Id == templateId, cancellationToken) ?? throw MissingTemplate();
    }

    private IQueryable<ChecklistTemplateItem> ActiveItems(ChecklistTemplate template) =>
        database.ChecklistTemplateItems.Where(item => item.TenantId == template.TenantId && item.TemplateId == template.Id && item.IsActive);

    private async Task<ChecklistTemplateItem> LoadItemAsync(ChecklistTemplate template, Guid itemId, CancellationToken cancellationToken) =>
        await ActiveItems(template).SingleOrDefaultAsync(item => item.Id == itemId, cancellationToken)
        ?? throw DetailRules.Missing("checklist_template_item_not_found", "The checklist template item was not found.");

    private static FieldOpsException MissingTemplate() =>
        DetailRules.Missing("checklist_template_not_found", "The checklist template was not found.");
}
