using FlowForge.Infrastructure.Email;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class EmailTemplateTests
{
    [Fact]
    public void Invitation_renders_a_named_button_with_the_original_destination()
    {
        var html = EmailTemplate.Render("FlowForge workspace invitation",
            "You have been invited.\n\nSign in before accepting:\nhttps://example.test/invitations/123#token=a&b=c\n\nThis invitation expires tomorrow.");
        Assert.Contains("href=\"https://example.test/invitations/123#token=a&amp;b=c\"", html);
        Assert.Contains(">Review invitation</a>", html);
        Assert.Contains("This invitation expires tomorrow.", html);
        Assert.Contains("text-align:center", html);
    }

    [Theory]
    [InlineData("Verify your FlowForge email")]
    [InlineData("Reset your FlowForge password")]
    public void Codes_preserve_leading_zeroes_and_expiry_instructions(string subject)
    {
        var html = EmailTemplate.Render(subject, "Your code is:\n\n012345\n\nIt expires in 10 minutes.");
        Assert.Contains(">012345</p>", html);
        Assert.Contains("It expires in 10 minutes.", html);
        Assert.DoesNotContain("<a ", html);
    }

    [Fact]
    public void User_content_is_escaped_and_only_http_links_become_buttons()
    {
        var html = EmailTemplate.Render("A <script> & title", "<img src=x onerror=alert(1)>\n\njavascript:alert(1)\n\nhttps://example.test/open?x=1&y=2");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.Contains("&lt;img", html);
        Assert.Contains("href=\"https://example.test/open?x=1&amp;y=2\"", html);
        Assert.Contains(">Open FlowForge</a>", html);
    }
}
