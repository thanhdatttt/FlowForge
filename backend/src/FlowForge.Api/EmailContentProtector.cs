using FlowForge.Application.Notifications;
using Microsoft.AspNetCore.DataProtection;

namespace FlowForge.Api;

public sealed class EmailContentProtector(IDataProtectionProvider provider) : IEmailContentProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("FlowForge.EmailDelivery.v1");
    public string Protect(string body) => protector.Protect(body);
    public string Unprotect(string body) => protector.Unprotect(body);
}
