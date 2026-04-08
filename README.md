### TAllgrass-like Agent AI
 API

## Overview
TAllgrass Agent AI is an API that provides intelligent agent capabilities for the Tallgrass platform. It enables autonomous task execution and decision-making through AI-powered workflows.

## Getting Started

### Prerequisites
- .NET 8.0 or higher
- Visual Studio or VS Code

### Starting and testing the API
```
Run tests and start the API
```bash
./run
```
Run the API
```bash
dotnet restore
dotnet run --project src/TallgrassAgentApi
```
The API will be available at `https://localhost:5119/dashboard.html`

## Running Tests
```bash
dotnet test --filter "Category!=Integration"
```
Run integration tests (using real ClaudeService)
```bash
dotnet test --filter "Category=Integration"
```

