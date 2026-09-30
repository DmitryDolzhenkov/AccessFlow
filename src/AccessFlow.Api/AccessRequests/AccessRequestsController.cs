using AccessFlow.AccessRequests;
using AccessFlow.Api.Identity;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccessRequestView>> Get(Guid id, CancellationToken cancellationToken)
    {
        var request = await service.GetAsync(User.GetUserId(), id, cancellationToken);
        return request is null ? NotFound() : request;
    }
}
