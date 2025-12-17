namespace Mnemo.Api.Tests;

/// <summary>
/// Collection fixture that shares a single WebApplicationFactory across all integration tests.
/// This prevents connection pool exhaustion when running tests against Supabase.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
