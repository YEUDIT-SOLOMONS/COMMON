using System.Net;
using System.Text.Json;

namespace PhotoRental.Tests.Api;

public class PipelineTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public PipelineTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task EveryResponse_HasCorrelationIdHeader()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.Single()));
    }

    [Fact]
    public async Task IncomingCorrelationId_IsEchoedBack()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        request.Headers.Add("X-Correlation-Id", "abc-123");

        var response = await _client.SendAsync(request);

        Assert.Equal("abc-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/loans");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ErrorResponse_IsUniformJsonWithCorrelationId()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/items/does-not-exist");
        request.Headers.Add("X-Correlation-Id", "err-1");

        var response = await _client.SendAsync(request);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal((int)response.StatusCode, json.GetProperty("statusCode").GetInt32());
        Assert.True(json.TryGetProperty("message", out _));
        Assert.Equal("err-1", json.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task InvalidBody_Returns400WithFieldErrors()
    {
        var response = await _client.PostAsync("/api/auth/register",
            new StringContent("{\"name\":\"\",\"email\":\"not-an-email\",\"password\":\"1\"}", System.Text.Encoding.UTF8, "application/json"));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = json.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Email", out _));
        Assert.True(errors.TryGetProperty("Password", out _));
        Assert.True(json.TryGetProperty("correlationId", out _));
    }
}
