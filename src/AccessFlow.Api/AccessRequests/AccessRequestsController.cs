using AccessFlow.AccessRequests;
using AccessFlow.Api.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AccessFlow.Api.AccessRequests;

[ApiController]
[Route("access-requests")]
public sealed class AccessRequestsController(AccessRequestService service) : ControllerBase
{
    public sealed record CreateAccessRequestBody(Guid BeneficiaryId, Guid SystemId, string? Justification);

    public sealed record CreatedAccessRequest(Guid Id);

    [HttpPost]
    public async Task<IActionResult> Create(CreateAccessRequestBody body, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            User.GetUserId(),
            new CreateAccessRequest(body.BeneficiaryId, body.SystemId, body.Justification),
            cancellationToken);

        return result switch
        {
            CreateAccessRequestResult.Created created =>
                CreatedAtAction(nameof(Get), new { id = created.Id }, new CreatedAccessRequest(created.Id)),
            CreateAccessRequestResult.Invalid invalid =>
                ValidationProblem(new ValidationProblemDetails(invalid.Errors.ToDictionary())),
            CreateAccessRequestResult.ActiveAccessRequestExists =>
                Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: "An Active Access Request for this Beneficiary and System already exists."),
            _ => throw new InvalidOperationException($"Unexpected result {result}."),
        };
    }

    public sealed record ApproveBody(string? Comment);

    // The comment is optional, so the whole body may be omitted (BR-13).
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveBody? body, CancellationToken cancellationToken)
    {
        var result = await service.ApproveAsync(User.GetUserId(), id, new ApproveAccessRequest(body?.Comment), cancellationToken);
        return ToActionResult(result);
    }

    public sealed record RejectBody(string? RejectionReason);

    // A missing body is a missing Rejection Reason: 400 from the service, like an empty one (BR-13).
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RejectBody? body, CancellationToken cancellationToken)
    {
        var result = await service.RejectAsync(User.GetUserId(), id, new RejectAccessRequest(body?.RejectionReason), cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult(DecideAccessRequestResult result) =>
        result switch
        {
            DecideAccessRequestResult.Decided => NoContent(),
            DecideAccessRequestResult.NotFound => NotFound(),
            DecideAccessRequestResult.Forbidden =>
                Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    detail: "Only the current System Owner can decide on this Access Request."),
            DecideAccessRequestResult.Invalid invalid =>
                ValidationProblem(new ValidationProblemDetails(invalid.Errors.ToDictionary())),
            DecideAccessRequestResult.NotPending =>
                Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: "Only a Pending Access Request can be decided."),
            _ => throw new InvalidOperationException($"Unexpected result {result}."),
        };

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccessRequestView>> Get(Guid id, CancellationToken cancellationToken)
    {
        var request = await service.GetAsync(User.GetUserId(), id, cancellationToken);
        return request is null ? NotFound() : request;
    }

    [HttpGet("{id:guid}/audit-log")]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntryView>>> GetAuditLog(Guid id, CancellationToken cancellationToken)
    {
        var auditLog = await service.GetAuditLogAsync(User.GetUserId(), id, cancellationToken);
        return auditLog is null ? NotFound() : Ok(auditLog);
    }
}
