# Project Spec — “Federated Auth & Security Audit Dashboard” (Revised Scope)

## 1) Goals
- Primary auth with **Okta** using **native ASP.NET Core OIDC middleware** (no OktaMvc).
- Add exactly **one** additional OpenID provider (**Google**, **Microsoft**, etc.).
- Simple **Security Audit Dashboard** that shows:
  - Left: login/logout events (claim-gated)
  - Right: role assignment events (claim-gated)
- Simple **Role Assignment** screen (pick a user, pick one of three roles, Save).
- Minimal **SQL Server**, **Blazor Server** UI, and **GraphQL**.


---

## 2) Tech Stack
- **.NET 8**, **Blazor Server**
- **Auth:** Cookie auth + `AddOpenIdConnect` (Okta + one of Google/Microsoft)
- **DB:** SQL Server
- **GraphQL:** Minimal schema with **HotChocolate**

---

## 3) Authentication & Authorization

### 3.1 Providers
- **Okta (primary)** via OIDC Auth Code + PKCE.
- **One** secondary provider: **Google** *or* **Microsoft Entra ID**.

### 3.2 Middleware (illustrative)
- Default scheme: `Cookies`
- OIDC handlers: `"Okta"` and `"Google"` or `"Microsoft"` or your choice.
- Token validated events used to **audit LoginSuccess**.
- **Sign‑in buttons** route to `/signin/okta` and `/signin/google|microsoft|etc`.

### 3.3 Claims & Policies
- **Claims (exact names)**
  - `Audit.ViewAuthEvents`
  - `Audit.RoleChanges`
- **Authorization policies**
  - `CanViewAuthEvents` → requires claim `permissions: Audit.ViewAuthEvents`
  - `CanViewRoleChanges` → requires claim `permissions: Audit.RoleChanges`  
    (Also required to **assign roles** via UI/GraphQL.)

### 3.4 Roles (exact names)
- `BasicUser` — default for all new users; **no** permissions.
- `AuthObserver` — has `Audit.ViewAuthEvents`.
- `SecurityAuditor` — has both `Audit.ViewAuthEvents` and `Audit.RoleChanges`.

> **Defaulting rule:** On **first successful login** (from either provider), create a local user and set role = `BasicUser`. A user **always has a role**.

### 3.5 Login/Logout
- Successful login → write **LoginSuccess** event (details include `provider=Okta|Google|Microsoft`).
- Local logout → write **Logout** event.

---

## 4) Data Model (SQL Server)

### 4.1 ER Overview
```
[Users] 1───* [SecurityEvents]
  |
  └───1 [Roles]
      └───* [RoleClaims] *───1 [Claims]
```

### 4.2 Tables

**Users**
| Column       | Type             | Notes                                           |
|--------------|------------------|-------------------------------------------------|
| Id (PK)      | uniqueidentifier | default NEWID()                                 |
| ExternalId   | nvarchar(200)    | OIDC `sub` or immutable external key            |
| Email        | nvarchar(320)    | unique                                          |
| RoleId (FK)  | uniqueidentifier | FK → Roles(Id); **non‑nullable** (always set)   |

**Roles**
| Column      | Type             | Notes                                                    |
|-------------|------------------|----------------------------------------------------------|
| Id (PK)     | uniqueidentifier | default NEWID()                                          |
| Name        | nvarchar(100)    | unique (`BasicUser`, `AuthObserver`, `SecurityAuditor`) |
| Description | nvarchar(200)    |                                                          |

**Claims**
| Column      | Type             | Notes                                |
|-------------|------------------|--------------------------------------|
| Id (PK)     | uniqueidentifier | default NEWID()                      |
| Type        | nvarchar(100)    | e.g., `permissions`                  |
| Value       | nvarchar(200)    | `Audit.ViewAuthEvents`, `Audit.RoleChanges` |

**RoleClaims** (role → claim mapping)
| Column       | Type             | Notes            |
|--------------|------------------|------------------|
| RoleId (PK1) | uniqueidentifier | FK Roles(Id)     |
| ClaimId(PK2) | uniqueidentifier | FK Claims(Id)    |

**SecurityEvents**
| Column          | Type             | Notes                                                                 |
|-----------------|------------------|-----------------------------------------------------------------------|
| Id (PK)         | uniqueidentifier | default NEWID()                                                       |
| EventType       | nvarchar(50)     | `LoginSuccess`, `Logout`, `RoleAssigned`                              |
| AuthorUserId    | uniqueidentifier | FK Users(Id) — who performed the action (self on login/logout)        |
| AffectedUserId  | uniqueidentifier | FK Users(Id) — subject of the change                                  |
| OccurredUtc     | datetime2        | default SYSUTCDATETIME()                                              |
| Details         | nvarchar(400)    | e.g., `provider=Okta`, or `from=AuthObserver to=SecurityAuditor`      |

**Notes**
- User **always** has a role; default to `BasicUser` on creation.

---

## 5) Audit Events

| Action                         | EventType     | Details example                                      |
|--------------------------------|---------------|------------------------------------------------------|
| Successful login               | LoginSuccess  | `provider=Okta` or `provider=Google|Microsoft`       |
| Logout                         | Logout        | `local sign-out`                                     |
| Assign/switch user role (A→B)  | RoleAssigned  | `from=AuthObserver to=SecurityAuditor` *(single event)* |

> Role changes **always** emit **one** `RoleAssigned` event.

---

## 6) Pages / UX (Lo‑Fi)

### 6.1 Sign‑In
```
+----------------------------------------------+
|   Welcome                                    |
|                                              |
|  [ Sign in with Okta ]  [ Sign in with X ]   |
+----------------------------------------------+
```
*(X = Google or Microsoft or whatever)*

### 6.2 Security Audit Dashboard (`/audit`)
```
+--------------------------------------------------------------+
| Security Audit Dashboard                                     |
|--------------------------------------------------------------|
| LEFT: Auth Events                         | RIGHT: Role Changes
| (needs Audit.ViewAuthEvents)              | (needs Audit.RoleChanges)
|-------------------------------------------|-------------------------
| [Timestamp] [User] [Event] [Details]      | [Timestamp] [Actor->Target] [Event] [Details]
| 2025-08-24T14:03Z j.doe LoginSuccess ...  | 2025-08-24T15:10Z a.admin->k.moss RoleAssigned
| ...                                       |   details: from=AuthObserver to=SecurityAuditor
| If no access:                              | If no access:
|   "Insufficient access. Contact your admin."|  "Insufficient access. Contact your admin."
+--------------------------------------------------------------+
```

### 6.3 Role Assignment (`/roles/assign`)
```
+----------------------------------------------+
| Assign User Role                             |
|----------------------------------------------|
| User: [ one user ]                           |
| Role: [ BasicUser | AuthObserver | SecurityAuditor ]
|                                              |
|                [ Save ]                      |
|----------------------------------------------|
| On Save:                                     |
|  - Always write a single RoleAssigned event, |
|    with Details indicating "from=<old> to=<new>".
+----------------------------------------------+
```

---

## 7) GraphQL (Minimal, Aligned to Scope)

**Queries**
- `users`: returns `{ id, email, role }`
- `roles`: returns `{ id, name }`
- `securityEvents`: returns a list events ordered by occuredUtc DESC (newest at top).
  - Enforcement:
    - Return **auth events** only if caller has `Audit.ViewAuthEvents`.
    - Return **role change events** only if caller has `Audit.RoleChanges`.

**Mutations**
- `assignUserRole(userId: ID!, roleId: ID!): AssignRoleResult!`
  - Requires `Audit.RoleChanges`.
  - Updates the user’s `RoleId`.
  - Emits exactly **one** `RoleAssigned` event with `from=<old> to=<new>` in `Details`.

---

## 8) Authorization Rules
- `/audit` requires authentication (any role).
- Left panel requires `CanViewAuthEvents` (claim `Audit.ViewAuthEvents`) otherwise show **“Insufficient access. Contact your admin.”**
- Right panel requires `CanViewRoleChanges` (claim `Audit.RoleChanges`) otherwise the same message.
- Role Assignment UI and `assignUserRole` mutation also require `Audit.RoleChanges`.

---

## 9) Acceptance Checks

1. **Auth**
   - Okta sign‑in works via native OIDC; secondary provider sign‑in works.
   - First login creates local `Users` row with `RoleId` → `BasicUser`.
   - `LoginSuccess` and `Logout` audit events are written.

2. **Roles & Claims**
   - Roles exactly: `BasicUser`, `AuthObserver`, `SecurityAuditor`.
   - Claims exactly: `Audit.ViewAuthEvents`, `Audit.RoleChanges`.
   - Role → claim mapping as specified.

3. **Audit**
   - Role changes always produce a single **RoleAssigned** event with details `from=X to=Y`.

4. **Security Audit Dashboard**
   - Route accessible to any authenticated user.
   - Left panel shows only to users with `Audit.ViewAuthEvents`; otherwise shows **“Insufficient access…”**.
   - Right panel shows only to users with `Audit.RoleChanges`; otherwise the same message.
   - No filters/sorting/pagination.

5. **Role Assignment Screen**
   - Selecting a user + one of the three roles and saving updates `Users.RoleId`.
   - Emits exactly one `RoleAssigned` event with the correct `AuthorUserId`, `AffectedUserId`, and `from`/`to` details.

6. **GraphQL**
   - `users`, `roles`, `securityEvents` exist; `securityEvents` respects claim gating and returns an unfiltered (but safely limited) list.
   - `assignUserRole` enforces `Audit.RoleChanges` and writes a single `RoleAssigned` event.

---
## Tests

1) Primary Sign‑In with Okta (Native OIDC)
    - Description: As a user, I can sign in using Okta through the native ASP.NET Core OIDC middleware.

    Acceptance Criteria:
    - “Sign in with Okta” initiates OIDC code flow and signs the user in with a cookie.
    - On first login, a local user record is created with role BasicUser.
    - A LoginSuccess event is recorded with AuthorUserId == AffectedUserId.
    - Sign‑out clears the cookie and records a Logout event.


2) Second Federated Provider (Google or Microsoft)
    - Description: As a user, I can alternatively sign in using a second OpenID provider.

    Acceptance Criteria:
    - A second button (“Sign in with Google” or “Sign in with Microsoft”) is present.
    - Successful sign‑in produces the same session outcome and audit events as Okta.
    - First‑time login via the second provider also provisions the user (if not already present).


3) Authorization Model: Roles & Claims Enforced
    - Description: As an admin, I want roles/claims seeded so the app enforces who can see which audit data.

    Acceptance Criteria:
    - DB contains roles BasicUser, AuthObserver, SecurityAuditor.
    - Claims exist: Audit.ViewAuthEvents, Audit.ViewRoleChanges.
    - AuthObserver maps to Audit.ViewAuthEvents; SecurityAuditor maps to both.
    - Authorization policies CanViewAuthEvents and CanViewRoleChanges are active.


4) Security Audit Dashboard (Read Panels with Claim Gating)
    - Description: As a user, I can open the Security Audit Dashboard and see panels based on my permissions.

    Acceptance Criteria:
    - Any authenticated user can open /audit.
    - Left panel (auth events) appears only to users with Audit.ViewAuthEvents; others see “Insufficient access. Contact your admin.”
    - Right panel (role changes) appears only to users with Audit.ViewRoleChanges; others see the same message.
    - Panels list timestamp, user(s), event type, and details; includes at least basic filters (e.g., last 7 days).


5) Role Assignment Screen (Single Role per User)
    - Description: As an admin, I can assign a single role to a user from a simple screen.

    Acceptance Criteria:
    - Screen provides a user picker and a role dropdown of the three roles.
    - Saving updates the user’s role (enforcing one role per user).
    - Correct audit event is written:
    - RoleAssigned (no previous role), RoleDeassigned (role removed), or RoleSwitched (A → B, single event).
    - Author vs. affected user IDs are recorded accurately.


6) Audit Logging Pipeline (Events Persisted)
    - Description: As a system owner, I want all login/logout and role changes to be persisted and queryable.

    Acceptance Criteria:
    - SecurityEvents table stores the five event types with UTC timestamps.
    - Login/logout are recorded for both providers, and role changes always emit exactly one event per action.


7) Minimal GraphQL API (Read Logs, Assign Role)
    - Description: As a client, I can query users/roles/security events and assign a role via GraphQL.

    Acceptance Criteria:
    - users, roles, and securityEvents queries are available; securityEvents respects claim‑based filtering.
    - assignUserRole(userId, roleId) mutation updates role and writes the correct single audit event.
    - Unauthorized calls are blocked by policies.
