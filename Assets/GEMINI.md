# Project Kizami (UsefulVr)

## Overview
A Unity VR project focused on high-performance mesh cutting/slicing features. It follows a decoupled architecture (Clean Architecture) and utilizes Unity's Job System for core logic.

## Architecture & Layers
The code is organized into several layers, each with its own Assembly Definition (`.asmdef`):
- **Domain:** Core business logic and entities. Pure C# (Unity math types like `Vector3` are allowed, but `MonoBehaviour` is forbidden).
- **Application:** Use cases and interfaces.
- **Infrastructure:** Implementations of Application interfaces using Unity features.
- **Presentation:** UI and presentation logic.
- **View:** Unity-specific `MonoBehaviour` components.
- **Composition:** Dependency injection and system entry points.
- **MeshBreak:** Specialized logic for mesh cutting.
- **Utility / UtilityUnity:** Helper classes.

## Development Conventions
- **Decoupling:** Keep `MonoBehaviour` and Unity-specific lifecycle logic out of the Domain and Application layers.
- **Math Types:** `Vector3`, `Vector2`, `Quaternion` are permitted in the Domain layer for convenience.
- **Code Generation:** Use the custom Editor tools (`Assets/Code/Editor`) to generate Enums for Audio, Scenes, and Input Actions.
- **Custom Attributes:**
    - `[ShowOnly]`: Displays values in the inspector without allowing edits.
    - `[SubclassSelector]`: Allows choosing implementations of an interface/abstract class from a dropdown in the inspector.

## Key Files & Directories
- `Assets/Code/Scripts/Domain/`: Core logic (e.g., `MovementLogic.cs`).
- `Assets/Code/Scripts/MeshBreak/`: Mesh cutting implementation (e.g., `MultiMeshCut.cs`).
- `Assets/Code/Editor/`: Custom editor tools and code generators.
- `Assets/Level/Scenes/`: Game scenes.
- `Assets/Code/AutoGenerate/`: Automatically generated code for Enums and Input Actions.

## Building & Running
- **Standard Unity Workflow:** Open the project in Unity Editor.
- **Target Platform:** VR (OpenXR/XRI).
- **Input System:** Uses Unity's New Input System.
- **TODO:** Specific build/test commands (e.g. via CLI) are not yet defined. Use Unity Editor for building.
