#!/bin/bash
# Format files after Edit/Write tool use.
# Runs `dotnet format` scoped to the changed file's solution, which is cheap
# because the change-scope filter keeps it to that one file.

payload=$(cat)
file_path=$(echo "$payload" | jq -r '.tool_input.file_path // empty' 2>/dev/null)

if [ -z "$file_path" ] || [ ! -f "$file_path" ]; then
    exit 0
fi

case "$file_path" in
    *.cs|*.xaml)
        # dotnet format is only available on machines with the .NET SDK.
        # Skip silently if it isn't there — no hard dep on the toolchain.
        if ! command -v dotnet >/dev/null 2>&1; then
            exit 0
        fi
        # Scope: format only this file. --include expects a repo-relative path.
        repo_root=$(git -C "$(dirname "$file_path")" rev-parse --show-toplevel 2>/dev/null)
        [ -z "$repo_root" ] && exit 0
        rel_path="${file_path#$repo_root/}"
        (cd "$repo_root" && dotnet format HVAKR.Revit.sln --include "$rel_path" >/dev/null 2>&1) || true
        ;;
esac
