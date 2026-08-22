# AI_MEMORY.md - Durable Project Knowledge

This file contains important discoveries, design decisions, and lessons learned while working on the LLMPicker project.

## Recent Code Quality Improvements
- Refactored MainWindow.xaml.cs to use ProviderManager and SettingsManager classes
- Removed magic numbers (provider indices 1, 2, 3) and replaced with named constants
- Improved separation of concerns between UI and business logic
- Centralized provider configuration in ProviderManager class
- Settings persistence moved to dedicated SettingsManager class

## Dynamic Model Discovery Implementation
- Added `GetOllamaModelsAsync()` method to fetch models via `/api/tags` endpoint
- Added `GetLlamaCppModelsAsync()` method to fetch models via `/models` endpoint
- Implemented refresh button UI for manual model discovery
- Added error handling with fallback to manual models.json configuration
- Integrated with existing settings management for persistent model selection

## Provider API Endpoints
- FoundryLocal already has dynamic model discovery via `/models` endpoint
- Ollama models can be fetched via `/api/tags` endpoint
- Llama.cpp models can be fetched via `/models` endpoint
- Both endpoints return JSON arrays of model names

## Key APIs
- FoundryLocal: `http://{host}:51331/v1/models`
- Ollama: `http://{host}:11434/api/tags`
- Llama.cpp: `http://{host}:8080/models`

## Error Handling Patterns
- Use try/catch blocks for network operations
- Provide fallback to manual configuration when APIs fail
- Show user-friendly error messages when providers are unavailable

## File Structure
- MainWindow.xaml.cs - Main UI logic (refactored)
- ProviderManager.cs - Provider-specific logic
- SettingsManager.cs - Settings persistence
- config.json - Host configuration
- models.json - Manual model list (to be replaced with dynamic discovery)