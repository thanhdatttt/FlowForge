namespace FlowForge.Application.Notifications;

public interface IEmailContentProtector
{
    string Protect(string body);
    string Unprotect(string body);
}
