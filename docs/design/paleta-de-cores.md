# Design System - Color Palette

## Overview

This application uses a modern productivity-focused color system inspired by tools like ClickUp, Jira, Linear, GitHub and Notion.

Goals:

- Professional
- Clean
- Minimal
- High Contrast
- Accessible (WCAG AA)
- Support both Light and Dark themes

---

# Brand Colors

| Token | Value |
|--------|---------|
| Primary | #5B5CEB |
| Primary Hover | #4B4CD8 |
| Primary Active | #3F40BF |
| Primary Soft | #EEF0FF |

The Primary color should be used for:

- Primary Buttons
- Active Navigation
- Links
- Selected States
- Focus Ring
- Progress Bars
- Charts
- Toggle Active State

Avoid using Primary as a page background.

---

# LIGHT THEME

## Background

| Token | Color | Usage |
|--------|---------|---------------------------|
| Background | #F7F8FC | Main page background |
| Surface | #FFFFFF | Panels |
| Card | #FFFFFF | Cards |
| Sidebar | #FFFFFF | Navigation |
| Modal | #FFFFFF | Dialogs |
| Hover | #F1F3F8 | Hover state |
| Selected | #EAEFFF | Selected rows/cards |

---

## Text

| Token | Color | Usage |
|--------|---------|---------------------------|
| Primary | #111827 | Titles |
| Secondary | #4B5563 | Normal text |
| Tertiary | #6B7280 | Supporting text |
| Disabled | #9CA3AF | Disabled text |
| On Primary | #FFFFFF | Text inside primary button |

---

## Borders

| Token | Color |
|--------|---------|
| Border | #E5E7EB |
| Divider | #EEF2F7 |

Use borders sparingly.

Spacing should create separation before borders.

---

# DARK THEME

## Background

| Token | Color | Usage |
|--------|---------|---------------------------|
| Background | #111827 | Main page |
| Surface | #1A2235 | Containers |
| Card | #202A3C | Cards |
| Sidebar | #161E2F | Navigation |
| Modal | #202A3C | Dialog |
| Hover | #293548 | Hover |
| Selected | #323F63 | Selected |

Never use pure black (#000000).

---

## Text

| Token | Color |
|--------|---------|
| Primary | #F9FAFB |
| Secondary | #D1D5DB |
| Tertiary | #9CA3AF |
| Disabled | #6B7280 |

---

## Borders

| Token | Color |
|--------|---------|
| Border | #334155 |
| Divider | #293548 |

---

# Semantic Colors

These colors are shared between both themes.

## Success

Color:

#22C55E

Use for:

- Success Alerts
- Completed Tasks
- Success Toasts
- Positive KPIs

---

## Warning

Color:

#F59E0B

Use for:

- Warning Alerts
- Pending Status
- Waiting Approval

---

## Error

Color:

#EF4444

Use for:

- Errors
- Validation
- Delete Actions
- Failed Requests

---

## Info

Color:

#3B82F6

Use for:

- Informational Messages
- Tips
- Links
- Notifications

---

# Buttons

## Primary Button

Background

#5B5CEB

Text

#FFFFFF

Hover

#4B4CD8

Pressed

#3F40BF

Disabled

Background

#D1D5DB

Text

#9CA3AF

---

## Secondary Button

Light

Background:

#FFFFFF

Border:

#D1D5DB

Hover:

#F3F4F6

Dark

Background:

#202A3C

Border:

#334155

Hover:

#293548

---

## Danger Button

Background

#EF4444

Hover

#DC2626

Text

#FFFFFF

---

# Inputs

Light

Background

#FFFFFF

Border

#D1D5DB

Focused Border

#5B5CEB

Placeholder

#9CA3AF

---

Dark

Background

#202A3C

Border

#334155

Focused Border

#6E72FF

Placeholder

#6B7280

---

# Navigation

Sidebar

Light

Background

#FFFFFF

Item Hover

#F1F3F8

Item Selected

#EEF0FF

Text

#4B5563

Active Text

#5B5CEB

---

Dark

Background

#161E2F

Item Hover

#293548

Item Selected

#323F63

Text

#D1D5DB

Active Text

#6E72FF

---

# Tables

Header

Light

#F9FAFB

Dark

#1A2235

Row Hover

Light

#F3F4F6

Dark

#293548

Selected Row

Light

#EEF0FF

Dark

#323F63

---

# Kanban

Todo

#94A3B8

In Progress

#3B82F6

Review

#A855F7

Testing

#F59E0B

Done

#22C55E

Blocked

#EF4444

---

# Priority Colors

Low

#10B981

Medium

#FBBF24

High

#F97316

Critical

#DC2626

---

# Charts

Series 1

#5B5CEB

Series 2

#3B82F6

Series 3

#10B981

Series 4

#F59E0B

Series 5

#A855F7

Series 6

#EC4899

---

# Focus Ring

Use:

2px solid #5B5CEB

Dark Mode:

2px solid #6E72FF

Never remove focus indicators.

---

# Shadows

Light

Small

0 1px 3px rgba(0,0,0,.08)

Medium

0 8px 24px rgba(0,0,0,.12)

Large

0 16px 40px rgba(0,0,0,.16)

Dark

Small

0 2px 8px rgba(0,0,0,.45)

Medium

0 8px 24px rgba(0,0,0,.55)

Large

0 16px 48px rgba(0,0,0,.65)

---

# Border Radius

Buttons

10px

Cards

16px

Inputs

10px

Dialogs

20px

Badges

999px

---

# Spacing

4px

8px

12px

16px

24px

32px

48px

64px

Always use the spacing scale.

---

# Typography Recommendation

Primary Font

Inter

Fallback

system-ui

Font Weight

Regular 400

Medium 500

Semibold 600

Bold 700

---

# General Design Principles

- Use white space instead of excessive borders.
- Keep layouts clean and uncluttered.
- Avoid gradients except for branding elements.
- Prefer flat surfaces with subtle shadows.
- Use the Primary color only to draw attention.
- Keep semantic colors reserved for feedback and status.
- Ensure all text and interactive elements meet WCAG AA contrast requirements.
- Maintain visual consistency between Light and Dark themes.