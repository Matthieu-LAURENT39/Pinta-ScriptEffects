# Pinta ScriptEffects Add-in
An add-in for [Pinta](https://github.com/PintaProject/Pinta) to allow easily writing scripts that can be applied as effects. 

## Installation
> **NOTE: This addin is only compatible with Pinta 3.1 and above.**

<!-- UNCOMMENT THIS ONCE IT'S ADDED TO THE ADDIN REPOS
### From the official repository
1. Open Pinta and go to `Add-ins > Add-in Manager`.
2. In the Gallery tab, select "ScriptEffects" and click "Install". -->

### From Github releases or automated builds
1. Download the latest release from the [releases page](https://github.com/Matthieu-LAURENT39/Pinta-ScriptEffects/releases), or from [an automated build](https://github.com/Matthieu-LAURENT39/Pinta-ScriptEffects/actions).
1. Open Pinta and go to `Add-ins > Add-in Manager`.
2. Click "Install from file..." (the button in the top left) and select the downloaded .mpack file.

### From source
1. Make sure you have `just` installed, as well as the .NET SDK version 8.0, and `mautil` (`dotnet tool install --global Mono.Addins.UtilTool`).
2. Clone the repository.
3. Run `just pack`, which will build the project and bundle it into a .mpack file (an addin installation package).
4. Follow the ["From Github releases or automated builds"](#from-github-releases-or-automated-builds) instructions, but using the .mpack file generated in the previous step.