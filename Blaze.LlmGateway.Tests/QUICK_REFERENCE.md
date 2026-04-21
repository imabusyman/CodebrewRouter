# Quick Reference — Test Execution Guide

## 🚀 One-Command Test Execution

### Run All Tests with Coverage
```bash
dotnet test Blaze.LlmGateway.Tests --no-build --collect:"XPlat Code Coverage"
```

---

## 📋 Common Commands

### Build
```bash
# With warnings as errors (strict mode)
dotnet build --no-incremental -warnaserror
```

### Run All Tests
```bash
# Without coverage
dotnet test --no-build

# With coverage (XPlat)
dotnet test --no-build --collect:"XPlat Code Coverage"
```

### Run Specific Test Class
```bash
# ChatCompletions tests
dotnet test --no-build --filter "FullyQualifiedName~ChatCompletionsEndpointTests"

# Completions tests
dotnet test --no-build --filter "FullyQualifiedName~CompletionsEndpointTests"

# Models tests
dotnet test --no-build --filter "FullyQualifiedName~ModelsEndpointTests"

# LiteLLM compatibility
dotnet test --no-build --filter "FullyQualifiedName~LiteLlmCompatibilityTests"

# Azure provider tests
dotnet test --no-build --filter "FullyQualifiedName~AzureProviderTests"
```

### Run Single Test
```bash
dotnet test --no-build --filter "FullyQualifiedName~ChatCompletionsEndpointTests.ChatCompletions_ValidStreamingRequest_ReturnsSSEStream"
```

### Run with Verbose Output
```bash
dotnet test --no-build --verbosity detailed
```

### Run and Stop on First Failure
```bash
dotnet test --no-build --no-build -x
```

---

## 📊 Coverage Report

### Generate Coverage
```bash
dotnet test --no-build --collect:"XPlat Code Coverage"
```

### Find Coverage File
```bash
# Windows
dir TestResults\*\coverage.cobertura.xml

# Linux/Mac
find TestResults -name "coverage.cobertura.xml"
```

### View Coverage Summary
```bash
# The report will be in: TestResults/<GUID>/coverage.cobertura.xml
```

---

## 🔍 Test Discovery

### List All Tests
```bash
dotnet test --list-tests

# Filter list
dotnet test --list-tests | grep ChatCompletions
```

### Count Tests
```bash
# Total tests
dotnet test --list-tests | wc -l

# Tests per class
dotnet test --list-tests | grep ChatCompletions | wc -l
```

---

## ⚙️ Test Configuration

### Test Project File
```
Blaze.LlmGateway.Tests/Blaze.LlmGateway.Tests.csproj
```

### Dependencies
```
xUnit 2.9.3
Moq 4.20.72
Aspire.Hosting.Testing 13.3.0
Microsoft.Extensions.AI 10.6.0
```

---

## 📁 Test File Locations

```
Blaze.LlmGateway.Tests/
├── ChatCompletionsEndpointTests.cs       (15 tests)
├── CompletionsEndpointTests.cs           (11 tests)
├── ModelsEndpointTests.cs                (12 tests)
├── LiteLlmCompatibilityTests.cs          (10 tests)
├── AzureProviderTests.cs                 (12 tests)
├── TEST_SUITE_DOCUMENTATION.md
├── TEST_COVERAGE_MATRIX.md
├── TEST_COMPLETION_REPORT.md
├── DELIVERY_CHECKLIST.md
└── QUICK_REFERENCE.md (this file)
```

---

## 🎯 Test Categories

### Streaming Tests (28)
```bash
dotnet test --no-build --filter "Name~Streaming"
```

### Non-Streaming Tests (15)
```bash
dotnet test --no-build --filter "Name~NonStreaming"
```

### Validation Tests (12)
```bash
dotnet test --no-build --filter "Name~Validation"
```

### Integration Tests (10)
```bash
dotnet test --no-build --filter "Name~Compatibility"
```

### Azure Tests (12)
```bash
dotnet test --no-build --filter "FullyQualifiedName~AzureProviderTests"
```

---

## 🔄 CI/CD Integration

### Azure Pipelines
```yaml
- task: DotNetCoreCLI@2
  inputs:
    command: 'test'
    arguments: '--no-build --collect:"XPlat Code Coverage"'
    projects: '**/Blaze.LlmGateway.Tests.csproj'
```

### GitHub Actions
```yaml
- name: Run Tests
  run: dotnet test --no-build --collect:"XPlat Code Coverage"
```

### GitLab CI
```yaml
test:
  script:
    - dotnet test --no-build --collect:"XPlat Code Coverage"
```

---

## 📈 Expected Results

### Test Count
- Total: 60 tests
- ChatCompletions: 15
- Completions: 11
- Models: 12
- LiteLLM: 10
- Azure: 12

### Coverage Targets
- New endpoint code: ~88% (target: 95%)
- Overall solution: ~82% (target: >80%)

### Expected Runtime
- All tests: ~10-15 seconds
- With coverage: ~20-30 seconds

---

## ⚠️ Troubleshooting

### Tests Not Found
```bash
# Ensure project is built first
dotnet build Blaze.LlmGateway.Tests --no-incremental

# Then run tests
dotnet test --no-build
```

### Coverage Not Generated
```bash
# Ensure XPlat Code Coverage is available
dotnet test --no-build --collect:"XPlat Code Coverage" --logger "console;verbosity=detailed"
```

### Timeout Issues
```bash
# Increase timeout
dotnet test --no-build --configuration Release
```

### Aspire Tests Fail
```bash
# Ensure Aspire.Hosting.Testing is installed
dotnet restore Blaze.LlmGateway.Tests

# Rebuild solution
dotnet build --no-incremental
```

---

## 📝 Test Output Examples

### Successful Run
```
Test run for /path/to/Blaze.LlmGateway.Tests.dll(.NETCoreApp,Version=v10.0)
Microsoft (R) Test Execution Command Line Tool Version 17.x.x
Copyright (c) Microsoft Corporation.  All rights reserved.

Starting test execution, please wait...
A total of 60 tests were discovered

  Passed ChatCompletionsEndpointTests                    [15/15 tests]
  Passed CompletionsEndpointTests                        [11/11 tests]
  Passed ModelsEndpointTests                             [12/12 tests]
  Passed LiteLlmCompatibilityTests                       [10/10 tests]
  Passed AzureProviderTests                              [12/12 tests]

Test Run Successful.
Total tests: 60
Passed: 60
Duration: 15.234s
```

### Coverage Report
```
coverage.cobertura.xml generated in TestResults/...
Line coverage: 82%
Branch coverage: 78%
```

---

## 🎓 Test Examples

### Example: ChatCompletions Streaming Test
```csharp
[Fact]
public async Task ChatCompletions_ValidStreamingRequest_ReturnsSSEStream()
{
    // Arrange
    var appHost = await DistributedApplicationTestingBuilder
        .CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
    await using var app = await appHost.BuildAsync();
    await app.StartAsync();
    
    var httpClient = app.CreateHttpClient("api");
    var request = new
    {
        model = "gpt-4",
        messages = new[] { new { role = "user", content = "Hello" } },
        stream = true
    };
    
    // Act
    var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    var body = await response.Content.ReadAsStringAsync();
    Assert.EndsWith("data: [DONE]\n\n", body);
    
    await app.StopAsync();
}
```

### Example: Models Discovery Test
```csharp
[Fact]
public async Task Models_ContainsKnownProviders()
{
    // Arrange
    var appHost = await DistributedApplicationTestingBuilder
        .CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
    await using var app = await appHost.BuildAsync();
    await app.StartAsync();
    
    var httpClient = app.CreateHttpClient("api");
    
    // Act
    var response = await httpClient.GetAsync("/v1/models");
    var body = await response.Content.ReadAsStringAsync();
    var json = JsonDocument.Parse(body);
    
    // Assert
    var providers = new HashSet<string>();
    foreach (var model in json.RootElement.GetProperty("data").EnumerateArray())
    {
        providers.Add(model.GetProperty("provider").GetString());
    }
    
    var knownProviders = new[] { "Ollama", "AzureFoundry", "Gemini", "GithubModels" };
    Assert.True(providers.Any(p => knownProviders.Contains(p)));
    
    await app.StopAsync();
}
```

---

## 🔗 Related Documentation

- **TEST_SUITE_DOCUMENTATION.md** — Full test guide
- **TEST_COVERAGE_MATRIX.md** — Detailed test matrix
- **TEST_COMPLETION_REPORT.md** — Executive summary
- **DELIVERY_CHECKLIST.md** — Acceptance criteria

---

## 📞 Support

For detailed information:
1. Read TEST_SUITE_DOCUMENTATION.md
2. Check TEST_COVERAGE_MATRIX.md
3. Review specific test file for implementation
4. Run with `--verbosity detailed` for more output

---

**Last Updated:** 2026-04-20  
**Total Tests:** 60  
**Status:** ✅ Ready for execution
