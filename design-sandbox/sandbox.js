/* Caelus 设计沙盒 — 主题/模式切换与演示交互
   切换方式：在 <html> 上写 data-theme / data-mode，tokens.css 级联响应。
   选择持久化到 localStorage，刷新后保持。 */
(function () {
  "use strict";

  var root = document.documentElement;
  var STORAGE_KEY = "caelus-sandbox";

  function loadState() {
    var fallback = { theme: "dark", mode: "standard" };
    var stored;
    try {
      var s = JSON.parse(localStorage.getItem(STORAGE_KEY) || "{}");
      stored = {
        theme: s.theme === "light" ? "light" : "dark",
        mode: ["standard", "competitive", "custom"].indexOf(s.mode) >= 0 ? s.mode : "standard"
      };
    } catch (e) {
      stored = fallback;
    }
    // URL 查询参数优先（?theme=light&mode=competitive），供截图矩阵与分享链接使用
    var q = new URLSearchParams(location.search);
    if (q.get("theme") === "light" || q.get("theme") === "dark") stored.theme = q.get("theme");
    if (["standard", "competitive", "custom"].indexOf(q.get("mode")) >= 0) stored.mode = q.get("mode");
    return stored;
  }

  function saveState(state) {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch (e) { /* file:// 下可能失败，忽略 */ }
  }

  var state = loadState();

  var MODE_TEXT = { standard: "巡航", competitive: "竞技", custom: "自定义" };

  function apply() {
    root.setAttribute("data-theme", state.theme);
    root.setAttribute("data-mode", state.mode);
    saveState(state);
    // 同步所有工具条上的选中态
    document.querySelectorAll("[data-sandbox-theme]").forEach(function (btn) {
      btn.classList.toggle("is-checked", btn.getAttribute("data-sandbox-theme") === state.theme);
    });
    document.querySelectorAll("[data-sandbox-mode]").forEach(function (btn) {
      btn.classList.toggle("is-checked", btn.getAttribute("data-sandbox-mode") === state.mode);
    });
    // 同步模式名文本（CaelusCore 中心、Hero 徽章、活动摘要）
    document.querySelectorAll("[data-mode-text]").forEach(function (el) {
      el.textContent = MODE_TEXT[state.mode];
    });
    document.querySelectorAll(".segment-host").forEach(moveIndicator);
  }

  /* 分段控件滑动指示器（复刻 SegmentedControl.xaml 的 SelectionIndicator） */
  function moveIndicator(host) {
    var indicator = host.querySelector(".segment-indicator");
    var checked = host.querySelector(".segment-item.is-checked");
    if (!indicator || !checked) return;
    indicator.style.left = checked.offsetLeft + "px";
    indicator.style.width = checked.offsetWidth + "px";
  }

  /* 分段控件点击（演示用，纯视觉状态） */
  document.addEventListener("click", function (ev) {
    var item = ev.target.closest(".segment-item");
    if (item) {
      var host = item.closest(".segment-host");
      if (host && !item.hasAttribute("data-sandbox-theme") && !item.hasAttribute("data-sandbox-mode")) {
        host.querySelectorAll(".segment-item").forEach(function (el) { el.classList.remove("is-checked"); });
        item.classList.add("is-checked");
        moveIndicator(host);
        return;
      }
    }
    var nav = ev.target.closest(".nav-item");
    if (nav && nav.hasAttribute("data-nav-demo")) {
      document.querySelectorAll(".nav-item[data-nav-demo]").forEach(function (el) { el.classList.remove("is-checked"); });
      nav.classList.add("is-checked");
      return;
    }
    var themeBtn = ev.target.closest("[data-sandbox-theme]");
    if (themeBtn) { state.theme = themeBtn.getAttribute("data-sandbox-theme"); apply(); return; }
    var modeBtn = ev.target.closest("[data-sandbox-mode]");
    if (modeBtn) { state.mode = modeBtn.getAttribute("data-sandbox-mode"); apply(); }
  });

  window.addEventListener("resize", function () {
    document.querySelectorAll(".segment-host").forEach(moveIndicator);
  });

  apply();
})();
