(() => {
  "use strict";

  // Run each feature independently — one feature throwing must never take the
  // rest of the page's interactivity down with it (learned the hard way: an
  // earlier version had a single top-to-bottom script where one bad
  // reference silently killed every listener registered after it).
  function safe(name, fn) {
    try {
      fn();
    } catch (err) {
      console.error(`[OmenCore site] "${name}" failed to initialize:`, err);
    }
  }

  const root = document.documentElement;

  /* ---------- Theme ---------- */
  safe("theme", () => {
    const themeToggle = document.getElementById("themeToggle");
    const THEME_KEY = "omencore-theme";

    function applyTheme(theme) {
      root.setAttribute("data-theme", theme);
      localStorage.setItem(THEME_KEY, theme);
    }

    const savedTheme = localStorage.getItem(THEME_KEY);
    if (savedTheme) {
      applyTheme(savedTheme);
    } else if (window.matchMedia && window.matchMedia("(prefers-color-scheme: light)").matches) {
      applyTheme("light");
    }

    themeToggle?.addEventListener("click", () => {
      const next = root.getAttribute("data-theme") === "light" ? "dark" : "light";
      applyTheme(next);
    });
  });

  /* ---------- Nav scroll state + back to top ---------- */
  safe("nav-scroll", () => {
    const nav = document.getElementById("nav");
    const toTop = document.getElementById("toTop");
    const onScroll = () => {
      nav?.classList.toggle("is-scrolled", window.scrollY > 12);
      toTop?.classList.toggle("is-visible", window.scrollY > 600);
    };
    window.addEventListener("scroll", onScroll, { passive: true });
    onScroll();
    toTop?.addEventListener("click", () => window.scrollTo({ top: 0, behavior: "smooth" }));
  });

  /* ---------- Mobile nav ---------- */
  safe("mobile-nav", () => {
    const navToggle = document.getElementById("navToggle");
    const mobilePanel = document.getElementById("mobilePanel");
    if (!navToggle || !mobilePanel) return;

    navToggle.addEventListener("click", () => {
      const isOpen = mobilePanel.classList.toggle("is-open");
      navToggle.setAttribute("aria-expanded", String(isOpen));
      document.body.style.overflow = isOpen ? "hidden" : "";
    });
    mobilePanel.querySelectorAll("a").forEach((a) => {
      a.addEventListener("click", () => {
        mobilePanel.classList.remove("is-open");
        navToggle.setAttribute("aria-expanded", "false");
        document.body.style.overflow = "";
      });
    });
  });

  /* ---------- Scrollspy ---------- */
  safe("scrollspy", () => {
    const sections = ["features", "showcase", "compare", "download", "changelog", "community", "support"]
      .map((id) => document.getElementById(id))
      .filter(Boolean);
    const navLinks = Array.from(document.querySelectorAll(".nav-links a"));
    if (!sections.length || !navLinks.length || !("IntersectionObserver" in window)) return;

    const spyObserver = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            const id = entry.target.id;
            navLinks.forEach((a) => a.classList.toggle("active", a.getAttribute("href") === `#${id}`));
          }
        });
      },
      { rootMargin: "-45% 0px -50% 0px", threshold: 0 }
    );
    sections.forEach((s) => spyObserver.observe(s));
  });

  /* ---------- Reveal on scroll (with a hard fail-safe) ---------- */
  safe("reveal", () => {
    const revealTargets = Array.from(document.querySelectorAll(".reveal, .reveal-stagger"));
    if (!revealTargets.length) return;

    const revealAll = () => revealTargets.forEach((el) => el.classList.add("is-visible"));

    if (!("IntersectionObserver" in window)) {
      revealAll();
      return;
    }

    const revealObserver = new IntersectionObserver(
      (entries, obs) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-visible");
            obs.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12 }
    );
    revealTargets.forEach((el) => revealObserver.observe(el));

    // Fail-safe: content must never stay invisible because a browser/embedder
    // throttled or never fired IntersectionObserver (seen in some background/
    // prerendered tab states). If anything is still hidden after a few
    // seconds, just show it — a missed fade-in beats vanished content.
    window.setTimeout(revealAll, 3000);
  });

  /* ---------- Count-up stats ---------- */
  safe("stat-counters", () => {
    const countEls = document.querySelectorAll(".count-up");
    const statsGrid = document.getElementById("statsGrid");
    if (!countEls.length || !statsGrid) return;

    let animated = false;
    function animateCount(el) {
      const target = parseInt(el.dataset.target, 10) || 0;
      const duration = 1400;
      const start = performance.now();
      function tick(now) {
        const p = Math.min((now - start) / duration, 1);
        const eased = 1 - Math.pow(1 - p, 3);
        el.textContent = Math.round(eased * target);
        if (p < 1) requestAnimationFrame(tick);
      }
      requestAnimationFrame(tick);
    }
    function runOnce() {
      if (animated) return;
      animated = true;
      countEls.forEach(animateCount);
    }

    if (!("IntersectionObserver" in window)) {
      runOnce();
      return;
    }
    const statsObserver = new IntersectionObserver(
      (entries, obs) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            runOnce();
            obs.disconnect();
          }
        });
      },
      { threshold: 0.4 }
    );
    statsObserver.observe(statsGrid);
    // Same fail-safe reasoning as the reveal animations above.
    window.setTimeout(runOnce, 3500);
  });

  /* ---------- Hero hotspots (tap support) ---------- */
  safe("hotspots", () => {
    document.querySelectorAll(".hotspot").forEach((hotspot) => {
      hotspot.addEventListener("click", (e) => {
        e.stopPropagation();
        const wasActive = hotspot.classList.contains("is-active");
        document.querySelectorAll(".hotspot.is-active").forEach((h) => h.classList.remove("is-active"));
        if (!wasActive) hotspot.classList.add("is-active");
      });
    });
    document.addEventListener("click", () => {
      document.querySelectorAll(".hotspot.is-active").forEach((h) => h.classList.remove("is-active"));
    });
  });

  /* ---------- Download tabs ---------- */
  safe("download-tabs", () => {
    document.querySelectorAll(".tab-btn").forEach((btn) => {
      btn.addEventListener("click", () => {
        const tab = btn.dataset.tab;
        document.querySelectorAll(".tab-btn").forEach((b) => {
          b.classList.toggle("is-active", b === btn);
          b.setAttribute("aria-selected", String(b === btn));
        });
        document.querySelectorAll(".tab-panel").forEach((p) => {
          p.classList.toggle("is-active", p.dataset.panel === tab);
        });
      });
    });
  });

  /* ---------- Copy to clipboard ---------- */
  safe("copy-buttons", () => {
    document.querySelectorAll(".copy-btn").forEach((btn) => {
      const original = btn.textContent;
      btn.addEventListener("click", async () => {
        const text = btn.getAttribute("data-copy") || "";
        try {
          await navigator.clipboard.writeText(text);
          btn.textContent = "Copied!";
          setTimeout(() => (btn.textContent = original), 1600);
        } catch {
          /* clipboard API unavailable/denied — silently ignore, text is still selectable */
        }
      });
    });
  });

  /* ---------- Footer year ---------- */
  safe("footer-year", () => {
    const yearEl = document.getElementById("year");
    if (yearEl) yearEl.textContent = new Date().getFullYear();
  });

  /* ---------- Live GitHub stats (progressive enhancement) ---------- */
  safe("github-stats", () => {
    const REPO = "theantipopau/omencore";
    const starEl = document.getElementById("starCount");
    const versionEl = document.getElementById("latestVersion");
    const navVersionEl = document.querySelector(".brand small");
    const downloadPrimary = document.getElementById("downloadPrimary");

    fetch(`https://api.github.com/repos/${REPO}`)
      .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
      .then((data) => {
        if (starEl && typeof data.stargazers_count === "number") {
          starEl.textContent = data.stargazers_count.toLocaleString();
        }
      })
      .catch(() => {
        /* keep the static "★" fallback already in the markup */
      });

    fetch(`https://api.github.com/repos/${REPO}/releases/latest`)
      .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
      .then((data) => {
        if (!data.tag_name) return;
        if (versionEl) versionEl.textContent = data.tag_name;
        if (navVersionEl) navVersionEl.textContent = data.tag_name;
        const asset = (data.assets || []).find((a) => /\.(exe|msi)$/i.test(a.name));
        if (asset && downloadPrimary) downloadPrimary.href = asset.browser_download_url;
      })
      .catch(() => {
        /* keep the static version fallback already in the markup */
      });
  });
})();
