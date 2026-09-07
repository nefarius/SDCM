# Authentication

Hardware Dashboard still uses Microsoft Entra **client credentials**. The Partner Center **Key** you
create on the User management page is that client secret. In sdcm it is the profile `key` field and
`--auth client-secret` (the default `auto` mode picks this when `key` is set). There is no separate
`--auth api-key` mode.

The preferred setup is to create the Entra application and key in Partner Center, then write them
with `sdcm config set`. Interactive browser sign-in and Azure managed identity are still available;
see [Other credential types](#other-credential-types).


## Create an API key in Partner Center

1. Open
   [Partner Center User management](https://partner.microsoft.com/en-us/dashboard/account/v3/usermanagement)
   and sign in with an Entra ID tenant account that has enough rights.
2. Click **Microsoft Entra applications**.
3. Click **Add Microsoft Entra application**.
4. In the **Add Microsoft Entra application** dialog, select **Create Microsoft Entra application**.
5. In the **Create Microsoft Entra application** drawer, set a name (for example
   `Driver-Signing-Automation`) and a reply URL that will not be used (for example
   `http://localhost:54345/`).
6. Click **Next**. On **Roles applicable to developer programs**, tick **Driver submitter**, then
   **Create**.
7. The app appears in the list as `Driver-Signing-Automation <GUID>`, type **Microsoft Entra Apps**,
   role **Driver submitter**.
8. Click the app. In the drawer, click **Add new key**. Copy and save the **Client ID** and **Key**
   (the key is shown once). Close the drawer.

**Driver submitter** is enough for product and submission work. If you later create shipping labels
from automation, add **Shipping Label owner** (and **Shipping Label promoter**, if offered) on the
same app.

`tenantId` is still required by sdcm. The **Add new key** screen typically shows only Client ID and
Key; take the Entra tenant GUID from the same app-details drawer or from the tenant directory.


## Write the profile with `sdcm config set`

```bash
sdcm config set --tenant-id <tenant-guid> --client-id <client-guid> --key <partner-center-key>
```

That creates or updates the `default` profile in `authconfig.json`. Use `--profile <name>` to write
a different profile, and `--config <path>` to target a specific file.

To rotate only the key:

```bash
sdcm config set --key <new-partner-center-key>
```

`sdcm config set` writes UTF-8 without a BOM. It never prints the key; it reports the file path and
which fields changed. At least one of `--tenant-id`, `--client-id`, or `--key` is required.

Then smoke-test (with `key` set, `auto` uses client-secret):

```bash
sdcm product list
```

To fail closed instead of falling through to interactive sign-in:

```bash
sdcm product list --auth client-secret
```

`sdcm config path` shows which file was resolved. `sdcm config init` still writes a starter file if
you would rather edit JSON by hand.


## Configuration model

Config is layered, each layer overriding the previous:

1. `appsettings.json` (shipped with the tool) - non-secret HTTP/AAD defaults
2. `authconfig.json` - your named credential profiles (gitignored, never packed into the tool)
3. Environment variables prefixed `SDCM_` (double-underscore for nesting, e.g.
   `SDCM_PROFILES__DEFAULT__CLIENTID`)
4. Command-line options

`authconfig.json` is probed for, in order (first match wins):

1. An explicit `--config <path>`
2. The current working directory
3. The per-user config directory: `%APPDATA%\sdcm` on Windows, `$XDG_CONFIG_HOME/sdcm` (or
   `~/.config/sdcm`) elsewhere
4. The tool's own installation directory (for copy-deployed/self-contained builds)

Run `sdcm config path` to see this chain resolved for your machine.

`authconfig.json` uses named profiles:

```json
{
  "profiles": {
    "default": {
      "tenantId": "00000000-0000-0000-0000-000000000000",
      "clientId": "00000000-0000-0000-0000-000000000000",
      "key": null,
      "managedIdentityClientId": null,
      "url": "https://manage.devcenter.microsoft.com",
      "urlPrefix": "v2.0/my"
    }
  }
}
```

Select a profile with `--profile <name>` (defaults to `default`).

In CI, prefer environment variables over a file on disk. See
[Submit and wait from CI](ci-signing.md).


## `--auth` and `--aad`

`--auth` selects the credential type:

| Value              | Behavior                                                                |
|--------------------|--------------------------------------------------------------------------|
| `auto` (default)   | Picks `managed-identity` if `managedIdentityClientId` is set, else `client-secret` if `key` is set, else `interactive` |
| `managed-identity` | Azure managed identity (requires `managedIdentityClientId` in the profile) |
| `client-secret`    | Entra app + Partner Center key / client secret (requires `key` in the profile) |
| `interactive`      | Interactive browser sign-in via MSAL, cached for reuse between runs      |

`--aad` controls how aggressively interactive sign-in prompts (only relevant with `--auth interactive`
or when `auto` falls back to it):

| Value                    | Behavior                                                       |
|--------------------------|------------------------------------------------------------------|
| `never` (default)        | Silent/cached only; fails if nothing is cached                   |
| `prompt`                 | Silent first, then an interactive account-selection prompt       |
| `always`                 | Always interactive, forcing login                                |
| `refresh-session`        | Force a silent token refresh, then interactive forced login if that fails |
| `select-account`         | Always show the account selection prompt                         |


## Other credential types

- **Interactive:** leave `key` and `managedIdentityClientId` blank. For `--auth interactive`, the
  app registration needs a loopback redirect URI such as `http://localhost` (MSAL listens on a
  loopback port). `--aad never` (the default) only uses a cached token and will fail if nothing is
  cached; use `--aad prompt` on a developer machine for the first sign-in.
- **Managed identity:** set `managedIdentityClientId` to a user-assigned identity and use
  `--auth managed-identity` (or `auto`) when running on Azure.

Environment-variable equivalents of the profile fields (useful when you do not want a file):

```text
SDCM_PROFILES__DEFAULT__TENANTID
SDCM_PROFILES__DEFAULT__CLIENTID
SDCM_PROFILES__DEFAULT__KEY
```
