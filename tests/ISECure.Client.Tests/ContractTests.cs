using System.Text.Json;
using ISECure.Models;
namespace ISECure.Client.Tests;
public class ContractTests
{
    [Fact]
    public void GeneratedLoginPreservesAccountFacts()
    {
        var response = JsonSerializer.Deserialize<LoginResp>("""{"ResponseCode":"00","ResponseText":"OK","Account":{"Type":"integrator","Features":["module.invoicing"],"Entitlements":["bank-simulator"]}}""");
        Assert.NotNull(response?.Account);
        Assert.Contains("module.invoicing", response.Account.Features);
    }
}
