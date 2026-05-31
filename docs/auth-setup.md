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
6. **Review + create**. The tenant takes a couple of minutes to provision.
7. Switch to the new tenant from the top-right tenant picker — the rest of this document happens **inside the SLYPN tenant**.

Note the tenant id — you'll need it for app settings later. **Identity → Overview → Properties → Tenant ID**.

---

## 2. Set up the user flow

A user flow is the policy that drives the hosted sign-up + sign-in pages.

1. **Identity → External Identities → User flows → New user flow**.
2. Name: `B2C_1_SLYPN_SignInSignUp`. (The portal forces the `B2C_1_` prefix for historical reasons; we'll refer to it as `slypn-signin-signup` everywhere else.)
3. **Identity providers** — leave **Email with password** selected. Google + Facebook are added in step 3.
4. **User attributes and token claims**:
   - Collect on sign-up: **Display Name**, **Email Address**.
   - Return in token: **Display Name**, **Email Address**, **User's Object ID**, **Identity Provider**.
   The Object ID is what our API uses as the canonical SLYPN member id.
5. **Application claims** — make sure `oid`, `email`, and `name` are returned. (Roles come from app-role assignments on the API app registration — that lands in #20.)
6. **Create**.

After creation, **Run user flow** (top of the policy blade) is the quickest smoke test once an app registration exists in #20.

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

The next sub-issue (#20) creates two app registrations against this tenant — one for the Vue SPA, one for the .NET API — and exposes the API scope + app roles (Admin / Contributor / Member). Those values flow into the SWA app settings; nothing goes into source control.

## Local dev against External ID

Local sign-in can talk to the real External ID tenant via MSAL — there's no offline emulator for External ID, but the free tier handles dev traffic comfortably. The Cosmos emulator (#17) still serves the data layer offline, so local dev only needs internet access for the OAuth dance.
