# SLYPN auth setup — Entra External ID

This is a **one-time manual** setup. After it's done, app registrations land in #20, the MSAL.js + JWT wiring in #21 and #22, and Vue route guards in #23.

> **Why Entra External ID?** It's Microsoft's modern customer-IAM service — the successor to Azure AD B2C. It supports email + password, Google, and Facebook out of the box, has 50,000 monthly active users free, and integrates cleanly with Azure Static Web Apps and Azure Functions. It is **not** the same as the workforce Entra tenant you may already have for your work email.

---

## 1. Create the External ID tenant

External ID lives in its own tenant — you can't mix customers with workforce users in the same tenant.

1. Sign in to the [Microsoft Entra admin centre](https://entra.microsoft.com/) with an account that can create resources in your Azure subscription.
2. **Overview → Manage tenants → Create**.
3. Choose **External**. (The other option, *Workforce*, is for employees and is not what we want.)
4. **Basics** — Organization name: `SLYPN`. Initial domain: `slypn` (yields `slypn.onmicrosoft.com`). Country: United Kingdom.
5. **Configuration** — choose **Customer**. Pick the closest Azure region (UK South).
6. **Subscription + resource group** — External ID tenants are billed against an Azure subscription, so the portal asks for both:
   - **Subscription**: the same one you'll deploy SLYPN to so the billing consolidates.
   - **Resource group**: create a new one called **`rg-slypn-identity`**. Keeping identity in its own RG is a convention — the SWA / Cosmos / Storage RG (`rg-slypn-dev`, `rg-slypn-prod`) can be torn down and re-deployed without losing the tenant. Reusing `rg-slypn-dev` also works but is harder to reason about later.

   The tenant itself doesn't consume RG resources; the RG just anchors the billing line item.
7. **Review + create**. The tenant takes a couple of minutes to provision.
8. Switch to the new tenant from the top-right tenant picker — the rest of this document happens **inside the SLYPN tenant**.

Note the tenant id — you'll need it for app settings later. **Identity → Overview → Properties → Tenant ID**.

---

## 2. Set up the user flow

A user flow is the policy that drives the hosted sign-up + sign-in pages.

1. **Identity → External Identities → User flows → New user flow**.
2. Name: `B2C_1_SLYPN_SignInSignUp`. (The portal forces the `B2C_1_` prefix for historical reasons; we'll refer to it as `slypn-signin-signup` everywhere else.)
3. **Identity providers** — leave **Email with password** selected. Google + Facebook are added in section 3.
4. **Create** the flow.
5. Open the flow and choose **User attributes**: tick **Display Name** and **Email Address**. Skip everything else — we don't need address / job title / etc.

> **Note on claims**
>
> In the modern Entra External ID portal there is **no separate "Application claims" tab**. Ticking attributes on the User attributes screen drives both what's collected on sign-up *and* what flows into the id token. `oid` is always returned without any setting, and the `roles` claim comes from app-role assignments on the API app registration in section 6.
>
> If you later notice that `email` or `name` isn't appearing in tokens, the fallback is **Identity → Applications → App registrations → `slypn-api` → Token configuration → + Add optional claim** (Token type: ID), and tick the missing claims there.

After saving, **Run user flow** (top of the policy blade) is the quickest smoke test once an app registration exists in section 6.

---

## 3. Add social identity providers

### Google

1. In the SLYPN External ID tenant: **External Identities → All identity providers → New OpenID Connect** (do **not** pick "Google" if a separate item appears — we want the OIDC variant for finer control).
2. In a separate browser tab, visit the [Google Cloud console](https://console.cloud.google.com/), create a new project `slypn-external-id`.
3. **APIs & Services → OAuth consent screen** — User type: **External**. App name: SLYPN. User support email: your email. Authorized domains: `slypn.onmicrosoft.com`. Add scopes: `openid`, `profile`, `email`.
4. **APIs & Services → Credentials → Create credentials → OAuth client ID**. Application type: **Web application**. Name: SLYPN External ID.
5. Authorized redirect URIs: `https://slypn.ciamlogin.com/<tenant-id>/oauth2/authresp`. (Find the exact value on the External ID provider page — copy it verbatim.)
6. Copy the **Client ID** and **Client secret** back to the Entra portal.
7. **Save** the OIDC provider in Entra.
8. Edit the `B2C_1_SLYPN_SignInSignUp` user flow and tick the Google IdP under **Identity providers**.

### Facebook

1. **External Identities → All identity providers → Facebook**.
2. In a separate tab, [Meta for Developers → My Apps → Create App](https://developers.facebook.com/). Type: **Consumer**. App name: SLYPN.
3. **Settings → Basic** — note the **App ID** + **App Secret**.
4. **Use cases → Authentication and account creation → Settings**. Add Facebook Login → Web.
5. Valid OAuth Redirect URIs: `https://slypn.ciamlogin.com/<tenant-id>/oauth2/authresp`.
6. App Domain: `ciamlogin.com`.
7. Site URL: `https://slypn.ciamlogin.com`.
8. Submit the app for App Review when you go to production. For dev, test users + a personal Facebook account are enough.
9. Paste the App ID + App Secret back into Entra. **Save**.
10. Tick Facebook on the user flow.

> **Heads up.** Facebook periodically tightens which apps can use Login; if the review step is blocking, swap in Microsoft account or Apple as a third option. The user flow lets you toggle providers independently.

---

## 4. Brand the sign-in page

1. **Identity → User experiences → Company branding → Customise**.
2. Background image: 1920×1080 PNG from `branding/`. (Use a desaturated background — the form sits in the centre.)
3. Banner logo: 36 px tall PNG with transparent background. Use a cropped version of `branding/SquareLogo.png`.
4. Square logo: 240×240 PNG for the favicon/lockscreen.
5. Sign-in page text: short single-line tagline — *"Working-age Parkinson's community in South London."*
6. Save. Re-run the user flow to verify the branding looks right on the hosted page.

CSS overrides are not necessary for v1; revisit if the default layout looks off on mobile.

---

## 5. What now sits in your hands

- **Tenant ID** (`Identity → Overview → Properties`).
- **User flow id** (`B2C_1_SLYPN_SignInSignUp`).
- **Google OAuth client id + secret** (in the Entra UI; do not commit).
- **Facebook app id + secret** (in the Entra UI; do not commit).

---

## 6. App registrations

Two registrations live in the SLYPN External ID tenant: one for the Vue SPA (public client, no secret), one for the API (token audience, with app roles). The SPA requests an access token for the API scope; the API validates it.

### 6.1 Register the API — `slypn-api`

1. **Identity → Applications → App registrations → New registration**.
2. Name: `slypn-api`.
3. Supported account types: **Accounts in this organizational directory only**.
4. Redirect URI: leave empty.
5. **Register**.

After registration:

1. **Overview** — note the **Application (client) ID**. Call it `apiClientId`.
2. **Expose an API → Application ID URI → Add** → accept the default `api://<apiClientId>` (or set a custom URI such as `api://slypn-api`). Save.
3. **Expose an API → Add a scope**:
   - Scope name: `access_as_user`.
   - Who can consent: **Admins and users**.
   - Admin consent display name: *"Access SLYPN on behalf of the signed-in user"*.
   - User consent display name: same.
   - State: **Enabled**. Add scope.
4. **App roles → Create app role** — repeat for each role:
   | Display name | Value | Allowed members |
   |---|---|---|
   | Administrator | `Admin` | Users/Groups |
   | Contributor | `Contributor` | Users/Groups |
   | Member | `Member` | Users/Groups |

   The **value** is the literal string our JWT validator looks for in the `roles` claim — it's case-sensitive.

### 6.2 Register the SPA — `slypn-spa`

1. **Identity → Applications → App registrations → New registration**.
2. Name: `slypn-spa`.
3. Supported account types: **Accounts in this organizational directory only**.
4. Redirect URI: **Single-page application (SPA)** → `http://localhost:5173/auth/callback`.
5. **Register**.

After registration:

1. **Overview** — note the **Application (client) ID**. Call it `spaClientId`.
2. **Authentication**:
   - Add the production redirect URI once the SWA is up: `https://<swa-default-hostname>/auth/callback`. (Placeholder until Phase 6.)
   - Add a logout redirect URI: `http://localhost:5173/` and the production equivalent.
   - **Implicit grant** — both checkboxes (Access tokens / ID tokens) **off**. PKCE is used instead.
   - **Allow public client flows** — off.
3. **API permissions → Add a permission → My APIs → slypn-api → Delegated permissions → access_as_user → Add**.
4. **Grant admin consent for SLYPN**. Confirm.

### 6.3 Assign roles to your first users

1. In the External ID tenant, **Identity → Applications → Enterprise applications → slypn-api → Users and groups → Add user/group**.
2. Pick yourself and assign the **Administrator** role.
3. Repeat for any additional admins / contributors. New customers who self-sign-up via the user flow start with no app role; the **Invite Member** flow in #24 grants them `Member` automatically.

---

## 7. Values to capture for code wiring

After steps 1–6 you should have the following — none go in source control, all go into app settings:

| Where it lives | Setting name | Source |
|---|---|---|
| Vue SPA (`VITE_*`) | `VITE_MSAL_AUTHORITY` | `https://slypn.ciamlogin.com/<tenantId>/v2.0` |
| Vue SPA | `VITE_MSAL_CLIENT_ID` | `spaClientId` |
| Vue SPA | `VITE_API_SCOPE` | `api://<apiClientId>/access_as_user` |
| Functions API | `AzureAd__Authority` | `https://slypn.ciamlogin.com/<tenantId>/v2.0` |
| Functions API | `AzureAd__Audience` | `api://<apiClientId>` |
| Functions API | `AzureAd__ValidIssuers__0` | `https://slypn.ciamlogin.com/<tenantId>/v2.0` |
| Functions API | `AzureAd__TenantId` | `<tenantId>` |

Sample placeholder values land in `src/web/.env.example` and `src/api/Slypn.Api/local.settings.sample.json` alongside the MSAL.js wiring in #21 and the JWT middleware in #22.

---

## 8. Local dev against External ID

Local sign-in talks to the real External ID tenant via MSAL — there's no offline emulator for External ID, but the free tier handles dev traffic comfortably. The Cosmos emulator (#17) still serves the data layer offline, so local dev only needs internet access for the OAuth dance.
