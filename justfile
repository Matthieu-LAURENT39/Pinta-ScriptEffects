PROJECT := "ScriptEffects/ScriptEffects.csproj"

# Build the plugin in debug mode
build-debug:
    dotnet build {{ PROJECT }}

# Build the plugin in release mode
build-release:
    dotnet build {{ PROJECT }} --configuration Release

# Cleans all build artifacts from the plugin and Pinta
clean:
    dotnet clean {{ PROJECT }}
    dotnet clean Pinta
    rm -f ScriptEffects.*.mpack
    rm -rf Pinta/build/bin/
    rm -rf ScriptEffects/bin/
    rm -rf ScriptEffects/obj/

# Runs Pinta with the plugin injected
run: build-debug
    # Make sure Pinta has been built
    dotnet build Pinta/Pinta
    # Inject the plugin's DLL
    @cp ScriptEffects/bin/Debug/net8.0/ScriptEffects.dll Pinta/build/bin/
    # Run Pinta
    dotnet run --project Pinta/Pinta

# Creates an mpack file (addin installation package) from the release build of the plugin
pack: build-release
    mautil pack ScriptEffects/bin/Release/net8.0/ScriptEffects.dll
