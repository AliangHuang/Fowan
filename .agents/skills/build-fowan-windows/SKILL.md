---
name: build-fowan-windows
description: 'Rebuild the public Fowan Toolbox and complete Windows tool suite into the unified runnable application tree. Use when asked to compile, rebuild, refresh, or verify Toolbox, Todo, Sticky Todo, Diary, AI Chat, AI Config, or all Fowan Windows tools; do not build FowanCore or create release packages.'
---

# Build Fowan Windows Tools

Build the public Fowan repository's complete runnable Windows application tree.

1. Work from the Fowan repository root. Inspect `git status --short` and preserve pre-existing changes.
2. Use `Debug` unless the user explicitly requests `Release`. A Release build is not release packaging; do not run `package-windows.ps1`, infer a version, or use `-Publish` unless the user explicitly requests the corresponding workflow.
3. Resolve the required Core artifact:

   ```text
   ..\FowanCore\out\core\windows\win-x64\debug\fowan-core.exe
   ..\FowanCore\out\core\windows\win-x64\release\fowan-core.exe
   ```

   Select the path matching the configuration. If it is missing, report the exact path and direct the user to `$build-fowan-core`; do not substitute a Cargo `target` executable or build private Core code from this Skill.
4. The build automatically stops only development-runtime processes whose exact executable paths are inside `build/windows/win-x64/app`, then restarts the applications that were running. It must not stop installed production processes. The first migrated build may also stop legacy unsuffixed executables from that same development directory.
5. Run the unified public build entry point:

   ```powershell
   .\scripts\build-windows.ps1 -Configuration <Debug|Release> [-CoreArtifactPath <absolute-path>]
   ```

6. Require zero warnings and zero errors. Confirm these runnable outputs exist under `build/windows/win-x64/app`:

   ```text
   Fowan.Windows.Dev.exe
   Tools/Todo/Fowan.Todo.Windows.Dev.exe
   Tools/Todo/Fowan.Todo.Sticky.Windows.Dev.exe
   Tools/Diary/Fowan.Diary.Windows.Dev.exe
   Tools/AI/Chat/Fowan.Ai.Chat.Windows.Dev.exe
   Tools/AI/Config/Fowan.Ai.Config.Windows.Dev.exe
   Core/fowan-core.Dev.exe
   ```

7. Report the configuration, Core artifact path, build result, unified output path, automatically stopped/restarted development processes, and pre-existing unrelated worktree changes.
