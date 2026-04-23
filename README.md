# HVAKR Revit Plugin

A set of projects for developing, testing, and deploying a Revit plugin using the Hvakr API and UI components.

---

## Prerequisites

- Install [Revit](https://www.autodesk.com/products/revit) (Although most iteration should be done using a standard windows app for speed)
- Install [Inno Setup](https://jrsoftware.org/download.php/is.exe?site=1)
- Install [Visual Studio](https://visualstudio.microsoft.com/vs/)

---

## Build Configurations

### **Debug**

- Builds the DLL in Debug mode only.
- No installer or deployment steps.

### **Release**

- Builds the DLL in Release mode.
- Copies the output to the `deploy` folder.
- Builds the installer:
  - See the following folders:
    - `deploy/plugin`
    - `deploy/installer`
  - The installer dynamically creates a `.addin` file pointing to the correct DLL path.
- Automatically runs the installer **silently** after building.

To test in Revit:

1. Set build configuration to `Release`
2. Build the solution
3. Open Revit

---

## 📁 Projects

| Project            | Description                                                          |
| ------------------ | -------------------------------------------------------------------- |
| **HvakrAPI**       | Core C# API for Hvakr functionality                                  |
| **HvakrAPI.Tests** | Unit tests for `HvakrAPI`                                            |
| **HVRevitPlugin3** | Main Revit plugin that loads into Revit and executes plugin logic    |
| **HvRevitUi**      | UI component/library used by the plugin to render the user interface |
| **HvRevitUiTest**  | Standalone Windows app to test and debug UI independently from Revit |
