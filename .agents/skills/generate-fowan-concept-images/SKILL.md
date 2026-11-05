---
name: generate-fowan-concept-images
description: Generate polished Fowan Windows product concept images with the current Fluent design language. Use when a Fowan requirement, proposal, feature, workflow, menu, dialog, empty state, or visual acceptance discussion needs one or more AI-generated UI concept images; default every concept image to 16:9 unless the user explicitly requests another ratio.
---

# Generate Fowan Concept Images

Generate review-ready concept images for Fowan Windows tools. Use the system `imagegen` skill for raster generation; this skill supplies the Fowan-specific discovery, prompt, screen-coverage, and handoff rules.

## Workflow

1. Read the relevant requirement, UI design, concept-image, and implementation documents before prompting. Inspect existing Fowan design assets when they control the requested screen. Do not invent product behavior that the source requirements do not establish.
2. Identify the minimum set of distinct screens needed to cover the request. Generate one image for each menu, dialog, mode, or result state that changes the user workflow materially. Do not substitute a collage for separate screens unless the user explicitly requests a single overview board.
3. Default every concept image to **16:9 landscape**. Use another aspect ratio only when the user specifies it or the target is intrinsically portrait, square, or mobile.
4. Build a concise production prompt for each screen. Include the current product name, target tool, screen purpose, screen-specific controls and exact visible labels, intended state, and exclusions.
5. Invoke the system `imagegen` skill in built-in mode. Generate each distinct screen with its own prompt. Inspect the generated image before delivering it; retry only the screen whose hierarchy, state, or important labels are wrong.
6. Present generated images inline for review. Save an image into the repository only when the user asks to retain it as a project asset; otherwise do not create or update product files.

## Fowan visual baseline

Unless a checked-in concept image or UI specification overrides it, prompt for a Windows 11 Fluent desktop application with restrained Fowan styling: clear information hierarchy, generous whitespace, rounded cards, subtle low-contrast borders, accessible contrast, and a calm dark or light surface chosen from the applicable requirement. Avoid browser chrome, mobile framing, people, fake logos, watermarks, unrelated dashboards, and decorative charts without a product need.

Use Chinese labels for Chinese Fowan tools. Quote text that must appear verbatim and limit each screen to the labels needed to communicate the interaction; generated typography must still be visually inspected for legibility.

## Prompt template

Use this structure, omitting fields that do not apply:

```text
Use case: ui-mockup
Asset type: Fowan Windows <tool> concept image
Primary request: <one screen and its user goal>
Style/medium: Windows 11 Fluent desktop UI, Fowan visual language
Composition/framing: 16:9 landscape full application window
Visible text: "<exact labels>"
Screen state: <selected navigation item, filter, empty/loading/success/error state>
Constraints: <requirements that must be visible>
Avoid: browser chrome, mobile UI, people, watermark, fake logo, unrelated charts
```

## Generation failure

If built-in image generation fails, state that no image was created and provide the complete, copyable prompt or prompt set to the user. Do not switch to a CLI/API fallback unless the user explicitly asks for it and confirms the required local credentials.
