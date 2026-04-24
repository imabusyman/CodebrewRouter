# microsoft/Foundry-Local Research Sources

This directory contains local mirrors of key files from the microsoft/Foundry-Local repository for reference and citation purposes.

## Repository Information

- **Repository**: [microsoft/Foundry-Local](https://github.com/microsoft/Foundry-Local)
- **Latest Commit SHA**: `857aa2242b7bb1c7411d1a8d932875464b6839ee`
- **License**: Microsoft Software License Terms

## Source Files

### Root Documentation

- `repo/README.md` — Main README with quickstart guides and feature overview
- `repo/LICENSE` — Microsoft Software License Terms
- `repo/SECURITY.md` — Security policy and reporting instructions

### SDK Documentation

- `repo/sdk/cs/README.md` — C# SDK reference (NuGet: Microsoft.AI.Foundry.Local)
- `repo/sdk/js/README.md` — JavaScript SDK reference (npm: foundry-local-sdk)
- `repo/sdk/python/README.md` — Python SDK reference (pip: foundry-local-sdk)
- `repo/sdk/rust/README.md` — Rust SDK reference

### Additional Resources

- `repo/docs/README.md` — Documentation index with links to Microsoft Learn

### Sample Directories (Ready to Populate)

- `repo/samples/cs/` — C# examples
- `repo/samples/js/` — JavaScript examples
- `repo/samples/python/` — Python examples
- `repo/samples/rust/` — Rust examples

## Research Document

The main research document using these sources is located at:

`../foundry-local-comprehensive.md`

All citations in the research document reference files in this directory, following the pattern:
```
[sources/microsoft-foundry-local/repo/path/to/file.md](./sources/microsoft-foundry-local/repo/path/to/file.md)
```

## How to Use These Sources

1. **Reading**: Open any .md file directly to review source material
2. **Citing**: Reference files use relative paths for portability
3. **Updating**: To refresh sources, re-download from the GitHub repository using the commit SHA above

## Integration with CodebrewRouter

Foundry Local is integrated into CodebrewRouter as a route destination:

- **Enum**: `Blaze.LlmGateway.Core.RouteDestination.FoundryLocal`
- **Endpoint**: `http://localhost:5273` (OpenAI-compatible)
- **Configuration**: `Blaze.LlmGateway.Api/appsettings.json`
- **Registration**: `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`

See the research document for detailed integration patterns.
