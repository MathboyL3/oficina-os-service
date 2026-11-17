using Reqnroll;

namespace Os.Bdd.Support;

[Binding]
public sealed class SagaHooks
{
    private readonly ScenarioContext _ctx;

    public SagaHooks(ScenarioContext ctx) => _ctx = ctx;

    [BeforeScenario]
    public void CriarFixture() => _ctx.Set(new SagaFixture());

    [AfterScenario]
    public async Task DerrubarFixture()
    {
        if (_ctx.TryGetValue<SagaFixture>(out var f)) await f.DisposeAsync();
    }
}