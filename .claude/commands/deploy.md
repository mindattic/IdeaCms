Deploy IdeaCms via **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`). MindAttic.Deploy is the source of truth for every MindAttic deploy; this command shims into it.

The deploy fires this repo's GitHub Actions workflow (`azure-deploy.yml`) by pushing `main`. One build ships to **two** sites on the same App Service plan:

- **https://mindattic.azurewebsites.net**, the company site.
- **https://mindattic-ideas-demo.azurewebsites.net**, the public vanilla demo.

The workflow builds Release, runs the full NUnit suite, migrates the company database and the demo template, deploys and restarts both sites, smoke-tests the company site, then runs `demo-reset.yml` (a fresh demo database, a new admin password, and a real sign-in check before the login is published).

Run this command and report the result:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --app ideas"
```

It will:

1. Run the `dotnet-build` pre-deploy hook against `IdeaCms.Blazor.csproj` (`-c Release`) to catch compile errors locally before pushing.
2. `git -C ../IdeaCms push origin main` if local commits are ahead of remote, triggering the Actions workflow.
3. Print the Actions URL for monitoring: <https://github.com/mindattic/IdeaCms/actions/workflows/azure-deploy.yml>.

After running, watch the run to completion with `gh run watch --repo mindattic/IdeaCms`. Then confirm `https://mindattic.azurewebsites.net/` and the demo both answer, and open the company site in the browser (`Start-Process https://mindattic.azurewebsites.net/`). Summarize which steps ran, what was pushed (or that nothing was), and the result of each job.

Notes:
- For a no-push rehearsal (build only), append `--dry-run`.
- **Watch what gets pushed.** This command commits whatever is staged in `IdeaCms` and pushes `main`. Commit or stash unrelated work first.
- **The demo resets on its own** every hour. To reset it now, dispatch `demo-reset.yml`: `gh workflow run demo-reset.yml --repo mindattic/IdeaCms`.
- **Content does not need a deploy.** A page, widget or theme goes live by uploading its `.idea` through Admin. Media goes in through Admin → Media or `--upload-media`. Deploy only when the engine itself changed.
- Infrastructure changes go through `./infra/provision.ps1 -ResourceGroup rg-mindattic-ideas`, not this command. Full runbook: `docs/DEPLOYMENT.md`.
