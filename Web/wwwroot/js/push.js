// [CHANGE: web push notifications] Registers the service worker on every page and drives the
// opt-in card (Views/Partials/_PushOptIn.cshtml) when it is on the page.
// Related: Web/wwwroot/sw.js, Web/Controllers/PushApiController.cs
(function () {
  "use strict";

  var supportsServiceWorker = "serviceWorker" in navigator;
  var supportsPush = supportsServiceWorker && "PushManager" in window && "Notification" in window;

  var isIos =
    /iPad|iPhone|iPod/.test(navigator.userAgent) ||
    (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1); // iPadOS reports as Mac
  var isStandalone =
    window.navigator.standalone === true ||
    (window.matchMedia && window.matchMedia("(display-mode: standalone)").matches);

  var registrationPromise = supportsServiceWorker
    ? navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(function (err) {
        console.warn("Service worker registration failed", err);
        return null;
      })
    : Promise.resolve(null);

  function urlBase64ToUint8Array(base64String) {
    var padding = "=".repeat((4 - (base64String.length % 4)) % 4);
    var base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
    var raw = window.atob(base64);
    var output = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) output[i] = raw.charCodeAt(i);
    return output;
  }

  function sameKey(subscription, keyBytes) {
    var current = subscription.options && subscription.options.applicationServerKey;
    if (!current) return true; // browser doesn't expose it — assume unchanged
    var a = new Uint8Array(current);
    if (a.length !== keyBytes.length) return false;
    for (var i = 0; i < a.length; i++) if (a[i] !== keyBytes[i]) return false;
    return true;
  }

  function initOptIn(card) {
    var publicKey = card.getAttribute("data-public-key");
    var token = card.getAttribute("data-token");
    var impersonating = card.getAttribute("data-impersonating") === "true";
    var message = card.querySelector("[data-push-message]");
    var keyBytes = publicKey ? urlBase64ToUint8Array(publicKey) : null;

    function show(state) {
      card.querySelectorAll("[data-push-state]").forEach(function (el) {
        el.hidden = el.getAttribute("data-push-state") !== state;
      });
    }

    function say(text, isError) {
      if (!message) return;
      message.textContent = text || "";
      message.hidden = !text;
      message.className = "mt-3 text-sm " + (isError ? "text-red-600" : "text-green-700");
    }

    function post(path, body) {
      return fetch("/umbraco/surface/push/" + path, {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", RequestVerificationToken: token },
        body: body ? JSON.stringify(body) : "{}",
      }).then(function (res) {
        return res
          .json()
          .catch(function () {
            return {};
          })
          .then(function (data) {
            if (!res.ok || data.success === false) throw new Error(data.message || "Serverfejl (" + res.status + ")");
            return data;
          });
      });
    }

    function busy(on) {
      card.querySelectorAll("button").forEach(function (b) {
        b.disabled = on;
        b.classList.toggle("opacity-50", on);
      });
    }

    if (impersonating) return show("impersonating");
    if (!supportsPush) return show(isIos && !isStandalone ? "ios-install" : "unsupported");
    if (Notification.permission === "denied") return show("denied");

    registrationPromise
      .then(function () {
        return navigator.serviceWorker.ready;
      })
      .then(function (reg) {
        return reg.pushManager.getSubscription().then(function (sub) {
          if (!sub) return show("off");
          // VAPID key rotated on the server → the old subscription can't be used any more
          if (!sameKey(sub, keyBytes)) {
            return sub.unsubscribe().then(function () {
              show("off");
            });
          }
          // Re-sync on every visit: keeps the server row fresh and repairs it if it was pruned
          show("on");
          return post("subscribe", sub.toJSON()).catch(function () {});
        });
      })
      .catch(function () {
        show("unsupported");
      });

    card.addEventListener("click", function (e) {
      var action = e.target.closest("[data-push-action]");
      if (!action) return;
      var kind = action.getAttribute("data-push-action");
      say("");

      if (kind === "enable") {
        busy(true);
        // requestPermission must be the first thing in the click handler — Safari requires a user gesture
        Notification.requestPermission()
          .then(function (permission) {
            if (permission !== "granted") {
              show(permission === "denied" ? "denied" : "off");
              throw new Error(permission === "denied" ? "" : "Du skal tillade notifikationer for at slå dem til.");
            }
            return navigator.serviceWorker.ready;
          })
          .then(function (reg) {
            return reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes });
          })
          .then(function (sub) {
            return post("subscribe", sub.toJSON());
          })
          .then(function () {
            show("on");
            say("Notifikationer er slået til på denne enhed.");
          })
          .catch(function (err) {
            if (!err || !err.message) return;
            // DOMExceptions from pushManager.subscribe() carry English browser text — e.g. private/incognito windows
            var fromBrowser = err.name && err.name !== "Error";
            say(
              fromBrowser
                ? "Kunne ikke slå notifikationer til i denne browser. Er du i et privat vindue? (" + err.message + ")"
                : err.message,
              true
            );
          })
          .finally(function () {
            busy(false);
          });
      }

      if (kind === "disable") {
        busy(true);
        navigator.serviceWorker.ready
          .then(function (reg) {
            return reg.pushManager.getSubscription();
          })
          .then(function (sub) {
            if (!sub) return;
            var endpoint = sub.endpoint;
            return sub.unsubscribe().then(function () {
              return post("unsubscribe", { endpoint: endpoint });
            });
          })
          .then(function () {
            show("off");
            say("Notifikationer er slået fra på denne enhed.");
          })
          .catch(function (err) {
            say(err.message || "Kunne ikke slå notifikationer fra.", true);
          })
          .finally(function () {
            busy(false);
          });
      }

      if (kind === "test") {
        busy(true);
        post("test")
          .then(function () {
            say("Testbesked sendt — den dukker op om et øjeblik.");
          })
          .catch(function (err) {
            say(err.message, true);
          })
          .finally(function () {
            busy(false);
          });
      }
    });
  }

  document.addEventListener("DOMContentLoaded", function () {
    var card = document.getElementById("push-optin");
    if (card) initOptIn(card);
  });
})();
