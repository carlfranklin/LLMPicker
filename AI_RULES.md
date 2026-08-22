# AI_RULES.md - Permanent Project Rules

This file contains permanent project rules, architecture, conventions, constraints, and instructions for the LLMPicker project.

## Architecture
- .NET 10 WPF desktop application
- Uses MVVM pattern separation
- Supports multiple LLM providers: Ollama, FoundryLocal, Llama.cpp
- Configuration stored in JSON files
- Environment variables set for GitHub Copilot CLI integration

## Key Classes
- `MainWindow` - Main UI window
- `ProviderManager` - Handles provider logic and URLs
- `SettingsManager` - Manages settings persistence
- `ProviderType` - Enum for provider types

## Provider URLs
- Ollama: `http://{host}:11434/v1`
- FoundryLocal: `http://{host}:51331/v1`
- Llama.cpp: `http://{host}:8080/v1`

## Configuration Files
- `config.json` - Host address configuration
- `models.json` - Available models list (manual configuration)
- `settings.json` - User settings and last selected models

## Environment Variables
- `COPILOT_PROVIDER_BASE_URL` - API endpoint URL
- `COPILOT_MODEL` - Model name

## Code Conventions
- Use named constants instead of magic numbers
- Separate provider logic from UI logic
- Use async/await for network operations
- Handle API failures gracefully with fallbacks