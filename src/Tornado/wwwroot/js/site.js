(() => {
    const root = document.documentElement;
    let observerAttached = false;
    const themeStorageKey = "tornado-theme";

    const getStoredTheme = () => {
        try {
            return localStorage.getItem(themeStorageKey);
        } catch {
            return null;
        }
    };

    const getPreferredTheme = () => {
        const stored = getStoredTheme();
        if (stored === "light" || stored === "dark") {
            return stored;
        }
        if (window.matchMedia?.("(prefers-color-scheme: dark)").matches) {
            return "dark";
        }
        return "light";
    };

    const applyTheme = (theme) => {
        root.dataset.theme = theme;
    };

    const measureTopbar = (topbar) => {
        const height = Math.ceil(topbar.getBoundingClientRect().height);
        if (height > 0) {
            root.style.setProperty("--topbar-height", `${height}px`);
        }
    };

    const attachObservers = (topbar) => {
        if (observerAttached) {
            return;
        }
        observerAttached = true;

        measureTopbar(topbar);

        if ("ResizeObserver" in window) {
            const resizeObserver = new ResizeObserver(() => measureTopbar(topbar));
            resizeObserver.observe(topbar);
        } else {
            window.addEventListener("resize", () => measureTopbar(topbar));
        }

        const mutationObserver = new MutationObserver(() => measureTopbar(topbar));
        mutationObserver.observe(topbar, { childList: true, subtree: true });
    };

    const findTopbar = () => document.querySelector(".topbar");

    const init = () => {
        applyTheme(getPreferredTheme());
        const topbar = findTopbar();
        if (topbar) {
            attachObservers(topbar);
            return true;
        }
        return false;
    };

    if (!init()) {
        const observer = new MutationObserver(() => {
            if (init()) {
                observer.disconnect();
            }
        });

        const startObserving = () => {
            observer.observe(document.body, { childList: true, subtree: true });
        };

        if (document.body) {
            startObserving();
        } else {
            document.addEventListener("DOMContentLoaded", startObserving, { once: true });
        }
    }

    window.tornadoScrollRelations = () => {
        const tables = document.querySelectorAll(".mini-table");
        tables.forEach((table) => {
            const target = table.querySelector(".mini-row.is-related, .mini-row.is-hovered");
            if (!target) {
                return;
            }

            const containerRect = table.getBoundingClientRect();
            const rowRect = target.getBoundingClientRect();
            const padding = 8;

            if (rowRect.top < containerRect.top) {
                table.scrollTop -= (containerRect.top - rowRect.top) + padding;
            } else if (rowRect.bottom > containerRect.bottom) {
                table.scrollTop += (rowRect.bottom - containerRect.bottom) + padding;
            }
        });
    };

    const bindMiniTableWheel = () => {
        document.querySelectorAll(".mini-table").forEach((table) => {
            if (table.dataset.wheelBound) {
                return;
            }
            table.dataset.wheelBound = "1";
            table.addEventListener("wheel", (event) => {
                const canScroll = table.scrollHeight > table.clientHeight + 1;
                if (!canScroll) {
                    event.preventDefault();
                }
            }, { passive: false });
        });
    };

    bindMiniTableWheel();

    const tableObserver = new MutationObserver(() => {
        bindMiniTableWheel();
    });

    if (document.body) {
        tableObserver.observe(document.body, { childList: true, subtree: true });
    } else {
        document.addEventListener("DOMContentLoaded", () => {
            tableObserver.observe(document.body, { childList: true, subtree: true });
        }, { once: true });
    }

    const bindThemeToggle = () => {
        const button = document.querySelector("[data-theme-toggle]");
        if (!button || button.dataset.themeBound) {
            return;
        }
        button.dataset.themeBound = "1";
        button.addEventListener("click", () => {
            const nextTheme = root.dataset.theme === "dark" ? "light" : "dark";
            applyTheme(nextTheme);
            try {
                localStorage.setItem(themeStorageKey, nextTheme);
            } catch {
                // Ignore storage failures.
            }
        });
    };

    bindThemeToggle();
    const themeObserver = new MutationObserver(() => bindThemeToggle());
    if (document.body) {
        themeObserver.observe(document.body, { childList: true, subtree: true });
    } else {
        document.addEventListener("DOMContentLoaded", () => {
            themeObserver.observe(document.body, { childList: true, subtree: true });
        }, { once: true });
    }


    window.tornadoCopyText = async (text) => {
        if (!text) {
            return;
        }
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(text);
            return;
        }
        const textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        document.execCommand("copy");
        textarea.remove();
    };

    window.tornadoFocus = (element) => {
        if (element && element.focus) {
            element.focus();
        }
    };

    window.tornadoScrollToId = (id) => {
        const target = document.getElementById(id);
        if (target) {
            target.scrollIntoView({ block: "start", behavior: "smooth" });
        }
    };

    window.tornadoDescribeScrollspy = (bodyEl, navEl) => {
        if (!bodyEl || !navEl) {
            return;
        }

        if (bodyEl.dataset.scrollspyAttached) {
            return;
        }
        bodyEl.dataset.scrollspyAttached = "1";

        const navButtons = Array.from(navEl.querySelectorAll("[data-section]"));
        const sectionMap = new Map(navButtons.map((btn) => [btn.dataset.section, btn]));
        const sections = Array.from(bodyEl.querySelectorAll("#describe-summary, .describe-section"));

        if (sections.length === 0) {
            return;
        }

        const setActive = (id) => {
            navButtons.forEach((btn) => {
                btn.classList.toggle("is-active", btn.dataset.section === id);
            });
        };

        const observer = new IntersectionObserver((entries) => {
            const visible = entries
                .filter((entry) => entry.isIntersecting)
                .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
            if (visible.length > 0) {
                const id = visible[0].target.id;
                if (sectionMap.has(id)) {
                    setActive(id);
                }
            }
        }, { root: bodyEl, threshold: [0.2, 0.5, 0.8] });

        sections.forEach((section) => observer.observe(section));
        if (sections[0] && sectionMap.has(sections[0].id)) {
            setActive(sections[0].id);
        }
    };

})();
