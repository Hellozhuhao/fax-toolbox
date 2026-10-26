# Fax Toolbox

<!-- impeccable:product-schema 1 -->

## Platform

web

The product ships first as a Windows desktop application: a WPF host renders the main HTML/CSS/JavaScript interface through WebView2 and owns native windows, credentials, and task lifetime. The embedded third-party pages and the mirrored Android screen are task content, not surfaces that the toolbox may arbitrarily restyle.

## Stack

Existing WPF / .NET 10 host, plain HTML/CSS/JavaScript, versioned native message bridge, Rust backend. Preserve the module protocol, task isolation, saved settings, and per-consumer AI state.

## Users

Publicly released to ordinary Windows users, including first-time users. Frequent users need efficient navigation and keyboard paths. Module and skin developers need documented, stable extension contracts. Public distribution and autonomous design decisions were explicitly confirmed by the product owner on 2026-10-26.

## Product Purpose

Bring independent tools into one coherent workspace. Users can find a tool, prepare its inputs, explicitly start a task, understand its progress, switch work without losing context, and recover from a failure.

## Operating Context

Agent is the principal working entry; the learning, proofreading, networking, mirroring, and developer tools also remain independently accessible. The interface is Chinese first. Windows resizing, high DPI, keyboard navigation, Chinese IME, dark and light appearances, and slow or unavailable services are ordinary conditions.

## Capabilities and Constraints

- Skins are a toolbox core feature, reached under Status, with built-in and imported packages, a global selection, and independent module overrides.
- Skin authors may restructure their own interface and registered interactions, but cannot change permission boundaries, conceal required approvals, or falsify task outcomes. Recovery stays available.
- Starting a tool does not silently authorize running its task. Project file writes and task-control approvals remain explicit.
- Backend, AI consumer, module, and external page state are distinct. A failed module must not block unrelated tools.
- Existing functionality remains available; no fabricated service health, sample results presented as real, or new backend capabilities implied by UI.
- The product owner delegated design choices and asked not to be interrupted with preference or approval questions for this redesign.

## Brand Commitments

Fax Toolbox / Fax 工具箱. A coherent, modern, readable interface with distinct but related module work areas. Richness comes from useful task-specific interaction and well-crafted graphics, not indiscriminate decoration or differently styled buttons.

## Evidence on Hand

AGENTS.md, PRODUCT_DESIGN.md, ARCHITECTURE.md, docs/SKINS.md, current executable source and the owner's screenshots. The screenshots expose inconsistent navigation icon backgrounds, module graphics, typography, and native dropdown presentation.

## Product Principles

1. Make current location, scope, task state, and the next useful action legible.
2. Keep common actions predictable across modules while allowing the work area's structure to match the task.
3. Prevent high-cost mistakes, preserve inputs on failure, and provide an accessible exit or recovery path.
4. Keep ordinary work simple and reveal specialist options in context.
5. Use one semantic design system across web controls, native windows, skins, and dynamic content.
