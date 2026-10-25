using System.Net;
using Cockpit.Shared;

namespace Cockpit.Client.Services;

public sealed class ApiException(HttpStatusCode status, ApiError error) : Exception(error.Message)
{
    public HttpStatusCode Status { get; } = status;
    public ApiError Error { get; } = error;
    public bool IsWipLimit => Error.Code == ApiError.WipLimitExceeded;
    public bool IsParkedNeedsRevisit => Error.Code == ApiError.ParkedRequiresRevisit;
}
