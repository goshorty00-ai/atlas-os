Design the **main Add-On Manager interface panel** for a futuristic AI-powered media centre.

The **sidebar and header already exist**, so generate **only the central content area**.

This interface manages **addon providers installed through JSON manifests, network discovery, and public addon registries**. The system focuses on discovering providers, installing addons, monitoring provider health, and using AI to rank the best providers.

The design must be **clean, structured, and professional**. Avoid clutter, floating UI, or overlapping elements.

---

LAYOUT RULES (very important)

Use a **12-column grid layout**.

All content must be arranged in **stacked rows with clear spacing**.

Each row can contain **maximum 2 or 3 panels**.

No floating widgets.
No overlapping cards.
No large decorative graphics.

All panels must have **consistent padding and margins**.

The interface should resemble a **modern developer dashboard or network control panel**.

---

VISUAL STYLE

Background: dark graphite or charcoal.

Accent colors:
soft neon blue
cyan highlights
subtle purple accents

Panels:
rounded corners
subtle glow border
minimal shadow

Typography:
clean modern sans-serif
clear hierarchy between headings and data.

---

TOP ROW — SYSTEM OVERVIEW

Three equal cards.

Card 1: Installed Add-Ons
Card 2: Available Providers
Card 3: Connected Networks

Each card contains:

large number metric
small label
minimal progress indicator

---

SECOND ROW — ADD-ON SOURCES

Three panels side-by-side.

Panel 1: Manifest Sources

Input field labeled "Add Manifest URL"

Buttons:

Add Manifest
Validate Manifest

Below show a list of added manifests displaying:

Addon Name
Manifest URL
Version
Install Button

---

Panel 2: Network Discovery

List showing discovered providers from:

Local Network
Private Nodes
Remote Provider Hosts

Each row shows:

Provider Name
Network Source
Latency
Install Button

Include a "Scan Network" button.

---

Panel 3: Add-On Registry

Shows providers available from a public addon registry.

Include a search bar.

Display addons in a **clean card grid**.

Each card includes:

Addon Name
Description
Category
Version
Install Button

---

THIRD ROW — INSTALLED ADD-ONS

Wide panel showing installed addons in a **grid layout**.

Each card contains:

Addon Name
Version
Provider Source
Enable / Disable Toggle
Update Button
Settings Button

Cards should be evenly spaced with consistent sizing.

---

FOURTH ROW — AI PROVIDER ANALYSIS

Two panels.

Panel 1: Provider Ranking

Table columns:

Rank
Provider Name
Source
Reliability Score
Latency Score
Overall Rating

Panel 2: Provider Performance

Bar chart showing provider reliability and response time.

---

FIFTH ROW — NETWORK STATUS

Two panels.

Panel 1: Network Nodes

List showing:

Local Network
Private Nodes
Remote Nodes

Each row displays:

Provider Count
Connection Health
Latency

Panel 2: Discovery Activity

Displays:

Last Scan Time
Providers Found
Active Discovery Status

Include simple progress bars.

---

MANIFEST INSPECTOR

When a provider or addon is clicked, open a **right-side slide panel**.

The panel displays:

Manifest URL
Provider Metadata
Capabilities
Version
Dependencies

Display the JSON in a **formatted developer-style viewer**.

Include buttons:

Copy
Refresh
Validate

---

INTERACTION STYLE

Animations must remain minimal.

Hover: slight elevation
Clickable rows: highlight on hover
Expandable sections: smooth accordion animation

Avoid large visual effects.

---

OVERALL GOAL

The interface should feel like a **clean control console for managing media provider addons**.

It should resemble a **modern system dashboard used to manage networks and services**, with a subtle futuristic style but strong focus on usability and structure.
