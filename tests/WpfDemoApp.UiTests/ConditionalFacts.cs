namespace WpfDemoApp.UiTests;

/// <summary>Runs only when an OpenAI-compatible endpoint is configured, otherwise reports as skipped.</summary>
public sealed class LiveEndpointFactAttribute : FactAttribute {
    public LiveEndpointFactAttribute() {
        if (!AppUnderTest.HasApiKey) {
            Skip = $"{AppUnderTest.ApiKeyVariable} is not configured, so the live endpoint test is skipped.";
        }
    }
}

/// <summary>Runs only when <em>no</em> API key is configured, i.e. the misconfiguration path.</summary>
public sealed class MissingApiKeyFactAttribute : FactAttribute {
    public MissingApiKeyFactAttribute() {
        if (AppUnderTest.HasApiKey) {
            Skip = $"{AppUnderTest.ApiKeyVariable} is configured, so the missing-key path is skipped.";
        }
    }
}
