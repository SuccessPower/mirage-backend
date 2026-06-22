(function () {
  "use strict";

  const storageKey = "mirage-swagger-theme";
  const darkClass = "swagger-dark";
  let currentTheme = "dark";
  let toggleButton;

  function preferredTheme() {
    try {
      const stored = window.localStorage.getItem(storageKey);
      if (stored === "light" || stored === "dark") return stored;
    } catch (_) {
      // Storage can be unavailable in hardened/private browser contexts.
    }
    return "dark";
  }

  function applyTheme(theme, button) {
    const isDark = theme === "dark";
    document.body.classList.toggle(darkClass, isDark);
    document.documentElement.style.colorScheme = isDark ? "dark" : "light";

    if (button) {
      button.textContent = isDark ? "☀ Light theme" : "☾ Dark theme";
      button.setAttribute("aria-pressed", String(isDark));
      button.setAttribute("aria-label", isDark ? "Switch to light theme" : "Switch to dark theme");
      button.title = isDark ? "Switch to light theme" : "Switch to dark theme";
    }
  }

  function persistTheme(theme) {
    try {
      window.localStorage.setItem(storageKey, theme);
    } catch (_) {
      // The toggle still works for the current page when storage is unavailable.
    }
  }

  function createToggle() {
    if (toggleButton) return toggleButton;

    toggleButton = document.createElement("button");
    toggleButton.id = "mirage-theme-toggle";
    toggleButton.type = "button";
    toggleButton.addEventListener("click", function () {
      currentTheme = document.body.classList.contains(darkClass) ? "light" : "dark";
      persistTheme(currentTheme);
      applyTheme(currentTheme, toggleButton);
    });
    applyTheme(currentTheme, toggleButton);
    return toggleButton;
  }

  function mountToggle() {
    const button = createToggle();
    const topbar = document.querySelector(".swagger-ui .topbar .topbar-wrapper");

    if (topbar) {
      if (button.parentElement !== topbar) topbar.appendChild(button);
      button.classList.add("is-mounted");
      return true;
    }

    if (!button.isConnected) document.body.appendChild(button);
    return false;
  }

  function initialize() {
    currentTheme = preferredTheme();
    applyTheme(currentTheme, createToggle());
    mountToggle();

    const observer = new MutationObserver(function () {
      if (mountToggle()) observer.disconnect();
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });

    window.setTimeout(function () {
      mountToggle();
      observer.disconnect();
    }, 10000);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize);
  } else {
    initialize();
  }
})();
