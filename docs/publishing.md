# Publishing a version of the .NET SDK

**English** | [Español](publishing.es.md)

This guide explains how to publish a new version of `InfiniAnalytics.Sdk` to
[nuget.org](https://www.nuget.org/packages/InfiniAnalytics.Sdk) and to GitHub Releases, and the
configuration behind it. Anyone on the team with the permissions listed below can do it without
depending on anyone else.

## Quick summary

```bash
git checkout main
git pull
git tag v0.2.0
git push origin v0.2.0
```

The `.github/workflows/ci.yml` workflow does the rest.

## Required permissions

| To... | You need |
|---|---|
| Publish a version (push a tag) | Write access to the `InfiniWorkspace/infinianalytics-.NET-sdk` repository |
| Change secrets, variables or the workflow | **Admin** role on the repository |
| Manage the package on nuget.org (unlist, deprecate) | Membership in the **InfiniAnalytics** organization on nuget.org |
| Manage organization members or Trusted Publishing policies | **Administrator** role in the **InfiniAnalytics** organization on nuget.org |

## Versioning

We follow [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`.

- **PATCH** (`0.1.0` to `0.1.1`): bug fixes with no changes to the public API.
- **MINOR** (`0.1.1` to `0.2.0`): new features that are backward compatible.
- **MAJOR** (`0.2.0` to `1.0.0`): breaking changes.
- **Prerelease**: `v1.0.0-beta.1`, `v1.0.0-rc.1`. NuGet marks them as prerelease and does not
  install them by default.

While the version is `0.x`, the API may change between minor versions.

The version is taken **from the tag** (`v0.2.0` becomes `0.2.0`). You do not need to change
`<Version>` in the `.csproj`; that value is only used for local builds.

## Before publishing

**A version published on nuget.org cannot be deleted or replaced.** It can only be hidden
(unlisted). If something goes wrong, you have to publish a new version.

Check that:

- `main` is up to date and the CI run of the commit you are going to tag is green.
- If there are breaking changes, you have increased the MAJOR version (or the MINOR version while
  we are on `0.x`).
- If there are changes that affect Power Automate Desktop, you have tested the
  [Power Automate Desktop guide](power-automate-desktop.md) in a new flow, with the DLL folder
  built by `dotnet build tools/DllBundle -c Release -o artifacts/dll-bundle`.
- You have tested the package locally (see below).

### Testing the package locally

Create the test project **outside the repository**. Inside it, the test project would inherit the
repository's `Directory.Build.props` (implicit usings are disabled, so the console template does
not compile) and `Directory.Packages.props` (`dotnet add package` would try to edit it).

From the repository root:

```bash
dotnet pack src/InfiniAnalytics -c Release -o ../nupkg-test -p:Version=0.2.0
cd ..
dotnet new console -o SdkTest
cd SdkTest
dotnet add package InfiniAnalytics.Sdk --source ../nupkg-test --version 0.2.0
dotnet build
```

NuGet keeps a copy of every package it installs. If you repeat the test with the same version
number, it uses that copy instead of the new package: delete the
`%USERPROFILE%\.nuget\packages\infinianalytics.sdk\0.2.0` folder (`~/.nuget/packages/...` on Linux
and macOS) before testing again.

## Publishing

1. Create the tag on the `main` commit you want to publish:
   ```bash
   git checkout main
   git pull
   git tag v0.2.0
   git push origin v0.2.0
   ```
2. Follow the run in the **Actions** tab of the repository.

### What the workflow does

When it receives a `v*` tag, `ci.yml` runs these jobs in order:

| Job | What it does |
|---|---|
| Build and test | Builds and runs the tests on Windows and Linux. |
| Pack | Builds the `.nupkg`, the `.snupkg` and the DLL zip with the version from the tag. |
| GitHub release | Creates the GitHub Release with those files attached. The release notes are generated from the commits. |
| Publish to NuGet | Gets a short-lived nuget.org API key through **Trusted Publishing** and publishes the package and its symbols. It only runs if the `NUGET_PUBLISH_ENABLED` variable is `true`. |

## After publishing

- nuget.org validates the package before listing it. It usually takes between 15 minutes and an
  hour, and the search can take a few more minutes. The symbol package is usually validated
  first. nuget.org sends an email to the account that published each one.
- Check the package page on nuget.org: version, README, icon and license.
- Check the Release on GitHub and that the DLL zip is attached. Review the generated release notes
  and edit them on GitHub if needed.

## If something goes wrong

| Problem | What to do |
|---|---|
| A job fails **before** "GitHub release" (tests, build, pack) | Fix the problem in `main` and recreate the tag on the new commit: `git tag -d v0.2.0`, `git push origin :refs/tags/v0.2.0`, then create and push it again. |
| "GitHub release" succeeds but "Publish to NuGet" fails because of configuration (login, policy, variable) | Do not touch the tag. Fix the configuration and click "Re-run failed jobs" in the run. |
| The tag has to point to another commit after the Release was created, but nothing reached nuget.org | Delete the Release (`gh release delete v0.2.0`) and the tag, then create the tag again. |
| The version is already on nuget.org but has a bug | It cannot be replaced. Unlist it on nuget.org (Manage package > Listing) and publish a patch version (`v0.2.1`). |
| Authentication error in the "NuGet login" step | See [Trusted Publishing](#trusted-publishing). The policy may be inactive, or `NUGET_USER` may not match the user who owns the policy. |
| The "Publish to NuGet" job is skipped | Check that the `NUGET_PUBLISH_ENABLED` variable is `true`. |

## Configuration

### GitHub (Settings > Secrets and variables > Actions)

| Type | Name | Value |
|---|---|---|
| Secret | `NUGET_USER` | nuget.org user name (profile name, not email) of the user who owns the Trusted Publishing policy |
| Variable | `NUGET_PUBLISH_ENABLED` | `true` to publish to nuget.org; any other value disables it |

We do not use long-lived API keys.

### Trusted Publishing

Instead of storing an API key as a secret, on each run the workflow asks nuget.org for a
short-lived key. nuget.org only grants it if the repository and the workflow match a configured
policy.

Current policy on nuget.org (your user > **Trusted Publishing**):

| Field | Value |
|---|---|
| Package Owner | `InfiniAnalytics` |
| Repository Owner | `InfiniWorkspace` |
| Repository | `infinianalytics-.NET-sdk` |
| Workflow File | `ci.yml` |
| Environment | (empty) |

**The policy belongs to the user who created it.** If that user leaves the InfiniAnalytics
organization on nuget.org, the policy stops working and publishing fails.

#### Recreating the policy

Any administrator of the organization can do it:

1. Sign in to nuget.org, open your user menu > **Trusted Publishing** and create a policy with the
   values in the table above.
2. On GitHub, change the `NUGET_USER` secret to your nuget.org user name.

If the workflow file or the repository is renamed, the policy has to be updated too, or
publishing fails.

### Organization on nuget.org

- Organization: **InfiniAnalytics**.
- There must always be **at least two administrators**, so that the package and the policy do not
  depend on a single person.
- Reserved ID prefix: `InfiniAnalytics.*`. Nobody outside the organization can publish packages
  with that prefix, and ours show the verified mark on nuget.org. It is managed by writing to
  account@nuget.org.

## Distribution

- **nuget.org**: for .NET projects and UiPath (`dotnet add package InfiniAnalytics.Sdk`).
- **GitHub Releases**: the DLL zip, for Power Automate Desktop and Blue Prism.
