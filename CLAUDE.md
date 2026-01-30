# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Dynamo is a visual programming tool built on C# and .NET 10. The codebase is split into two main solutions:
- **DynamoCore.sln** - Core engine without UI dependencies (cross-platform)
- **Dynamo.All.sln** - Full solution including WPF UI components (Windows-only)

## Build Commands

### Prerequisites
- Visual Studio 2022
- .NET 10 SDK
- Node.js LTS and npm (for some view extensions)

### Building DynamoCore (engine only)
```powershell
# Restore dependencies
dotnet restore src\DynamoCore.sln --runtime=win-x64 -p:Configuration=Release -p:DotNet=net10.0

# Build with MSBuild
msbuild src\DynamoCore.sln /p:Configuration=Release
```

### Building Dynamo.All (full application)
```powershell
# Restore dependencies
dotnet restore src\Dynamo.All.sln --runtime=win-x64 -p:Configuration=Release -p:DotNet=net10.0

# Build with MSBuild
msbuild src\Dynamo.All.sln /p:Configuration=Release
```

Output directory: `bin\AnyCPU\Release\`

### Building for Linux
```bash
# Restore dependencies
dotnet restore src/DynamoCore.sln --runtime=linux-x64 -p:Configuration=Release -p:Platform=NET_Linux -p:DotNet=net10.0

# Build
dotnet build src/DynamoCore.sln -c Release /p:Platform=NET_Linux
```

Output directory: `bin/NET_Linux/Release/`

## Running Tests

Tests use NUnit 3. Test projects are located in `test/` directory:
- **DynamoCoreTests** - Core engine tests
- **DynamoCoreWpfTests** - UI-related tests
- **Engine/** - Language runtime tests
- **Libraries/** - Built-in node tests

### Run all tests
```powershell
dotnet test src\Dynamo.All.sln --configuration Release
```

### Run specific test assembly
```powershell
# From output directory
dotnet test bin\AnyCPU\Release\DynamoCoreTests.dll
```

### Run tests by category
```powershell
dotnet test --filter "TestCategory~UnitTest"
```

Test base classes inherit from `DynamoModelTestBase` which provides utilities like `RunModel()` and `AssertPreviewValue()` for testing Dynamo graphs.

## Architecture

### Core Components

**DynamoCore** (`src/DynamoCore/`)
- The heart of Dynamo - language-agnostic graph execution engine
- Key classes:
  - `DynamoModel` - Main model coordinating graph execution
  - `WorkspaceModel` - Represents a Dynamo graph/script
  - `NodeModel` - Base class for all nodes
- Contains: Graph management, Scheduler, Search, Package management

**Engine** (`src/Engine/`)
- DesignScript language runtime
- **ProtoCore** - Core compiler and VM
- **ProtoAssociative** - Associative (dataflow) language parser
- **ProtoImperative** - Imperative language parser
- Handles code generation, AST, and execution

**DynamoCoreWpf** (`src/DynamoCoreWpf/`)
- WPF-based UI layer
- ViewModels for MVVM architecture
- Communicates with DynamoCore via commands/events
- Includes 3D visualization using HelixToolkit

**Libraries** (`src/Libraries/`)
- Built-in node libraries:
  - **CoreNodes** - Basic nodes (String, List, Math, etc.)
  - **CoreNodeModels** - Node models for built-in nodes
  - **GeometryUI** - Geometry manipulation nodes
  - **PythonNodeModels** - Python scripting nodes
  - **UnitsNodeModels** - Unit conversion nodes
  - **DSCPython** - Python engine integration

**ViewExtensions** (`src/*ViewExtension*/`)
- Plugin architecture for extending the UI
- Implement `IViewExtension` interface
- Examples:
  - **LibraryViewExtensionWebView2** - Node library browser
  - **PackageDetailsViewExtension** - Package manager UI
  - **PythonMigrationViewExtension** - Python 2 to 3 migration
  - **LintingViewExtension** - Graph analysis and warnings
- ViewExtensions are loaded dynamically and can add menu items, panels, and custom UI

**DynamoSandbox** (`src/DynamoSandbox/`)
- Standalone Dynamo application (no host like Revit)
- Entry point: `Program.cs`
- Uses `DynamoCoreSetup` to initialize the application

**DynamoPackages** (`src/DynamoPackages/`)
- Package manager functionality
- Package discovery, download, and loading
- Integrates with package feeds

**NodeServices** (`src/NodeServices/`)
- Base services for Zero-Touch and NodeModel node types
- Attributes for marking methods as nodes (`[IsVisibleInDynamoLibrary]`)

### Key Patterns

**Node Types:**
1. **Zero-Touch Nodes** - Methods marked with attributes, automatically imported
2. **NodeModel Nodes** - Custom C# classes inheriting from `NodeModel`
3. **Custom Nodes** - User-created graphs saved as reusable nodes

**Command Pattern:**
- UI actions are encapsulated as `RecordableCommand` objects
- Enables undo/redo functionality
- Commands executed through `DynamoModel.ExecuteCommand()`

**Scheduler:**
- Manages graph execution with support for both synchronous and asynchronous evaluation
- Handles node dirtying and evaluation order

## Development Guidelines

### Coding Standards
- Follow [Dynamo Coding Standards](https://github.com/DynamoDS/Dynamo/wiki/Coding-Standards)
- Follow [Naming Standards](https://github.com/DynamoDS/Dynamo/wiki/Naming-Standards)
- XML documentation comments required for all public methods/properties
- Configuration in `src/Config/CS_SDK.props` sets common build properties

### Adding New Nodes

When adding a new node (method with `[IsVisibleInDynamoLibrary(true)]`):

1. **Code** - Implement the node logic
2. **Documentation** - Add to `doc/distrib/NodeHelpFiles/`:
   - `.dyn` file - Sample graph demonstrating usage
   - `.md` file - Markdown documentation
   - `.jpg` file - Visual preview
3. **Localization** - Add strings to appropriate `.resx` files
4. **Tests** - Add unit tests covering the node's functionality

### API Compatibility
- Maintain backwards compatibility following semantic versioning
- Breaking changes require discussion via GitHub issue
- Track API surface in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`

### Pull Requests
- Use [Dynamo PR templates](https://github.com/DynamoDS/Dynamo/wiki/Choosing-a-Pull-Request-Template)
- Must include tests for new features or bug fixes
- Start with a GitHub issue for large changes
- See `.github/copilot-instructions.md` for PR review criteria

### Commit Messages
```
Summarize change in 50 characters or less

Provide more detail after the first line. Leave one blank line below the
summary and wrap all lines at 72 characters or less.

Fix #42
```

## Important Files

- `global.json` - Specifies .NET SDK version (10.0.100)
- `src/Config/CS_SDK.props` - Common build configuration
- `Directory.Build.props` - Solution-wide properties
- `.editorconfig` - Code formatting rules
- `extern/` - External dependencies (ProtoGeometry, etc.)

## Useful Links

- [Wiki](https://github.com/DynamoDS/Dynamo/wiki)
- [Developer Resources](https://developer.dynamobim.org/)
- [API Changes](https://github.com/DynamoDS/Dynamo/wiki/API-Changes)
- [Zero-Touch Plugin Development](https://github.com/DynamoDS/Dynamo/wiki/Zero-Touch-Plugin-Development)
- [DynamoSamples](https://github.com/DynamoDS/DynamoSamples)
- [Forum](https://forum.dynamobim.com)
