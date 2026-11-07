---
title: Anchor navigation
description: Scroll to the element named by the fragment of the URL in a Blazor application, on load and on navigation.
---

In a Blazor application, a link such as `page#usage` does not scroll to the element with the `usage` id: the router
handles the navigation and the browser does not. Add [AnchorNavigation](cref:Meziantou.AspNetCore.Components.AnchorNavigation) to the page, or to the layout for every page:

<!-- ref:../../../samples/BlazorAppSample/Documentation/AnchorNavigationExample.razor;format=md-fenced-code-block -->
```razor
@page "/documentation/anchor-navigation"

<nav>
    <a href="documentation/anchor-navigation#installation">Installation</a>
    <a href="documentation/anchor-navigation#usage">Usage</a>
</nav>

<h2 id="installation">Installation</h2>
<p>...</p>

<h2 id="usage">Usage</h2>
<p>...</p>

<AnchorNavigation />
```
<!-- endref -->

- When the component is first rendered, it scrolls to the element whose `id` is the fragment of the URL, so a link
  opened from outside the application lands on the right section.
- After that, it scrolls each time the location changes.
- The scrolling is smooth and aligns the element with the top of the view. Nothing happens when no element has that
  `id`.
- Text fragments (`#usage:~:text=install`) are supported: the part before `:~:` is used as the `id`.

The component renders nothing. It only scrolls once the component is interactive.
