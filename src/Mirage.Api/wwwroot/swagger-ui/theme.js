(function () {
  "use strict";

  const storageKey = "mirage-swagger-theme";
  const darkClass = "swagger-dark";

  function preferredTheme() {
    const stored = localStorage.getItem(storageKey);
    if (stored === "light" || stored === "dark") return stored;
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
    }
  }

  function initialize() {
    const button = document.createElement("button");
    button.id = "mirage-theme-toggle";
    button.type = "button";

    let theme = preferredTheme();
    applyTheme(theme, button);

    button.addEventListener("click", function () {
      theme = document.body.classList.contains(darkClass) ? "light" : "dark";
      localStorage.setItem(storageKey, theme);
      applyTheme(theme, button);
    });

    document.body.appendChild(button);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize);
  } else {
    initialize();
  }
})();
