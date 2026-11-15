// [CHANGE: web push notifications] Service worker for push notifications.
// Lives at the site root so its scope covers every page. No offline caching on purpose:
// the portal shows live data, and a stale cached page would be worse than no page.
// Related: Web/wwwroot/js/push.js, Code/Services/PushNotificationService.cs

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

self.addEventListener("push", (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { body: event.data ? event.data.text() : "" };
  }

  // Every push must show a notification (userVisibleOnly) — Safari revokes the subscription otherwise
  event.waitUntil(
    self.registration.showNotification(data.title || "Blue Bridge Frivillig", {
      body: data.body || "",
      icon: "/img/pwa/icon-192.png",
      badge: "/img/pwa/badge-96.png",
      tag: data.tag || undefined,
      data: { url: data.url || "/" },
    })
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();

  // Only ever navigate within this site
  let target;
  try {
    target = new URL((event.notification.data && event.notification.data.url) || "/", self.location.origin);
    if (target.origin !== self.location.origin) target = new URL("/", self.location.origin);
  } catch {
    target = new URL("/", self.location.origin);
  }

  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
      for (const client of windows) {
        if (new URL(client.url).origin === self.location.origin && "focus" in client) {
          await client.focus();
          if ("navigate" in client) {
            try {
              await client.navigate(target.href);
            } catch {
              // navigate() fails for uncontrolled clients — fall through to openWindow
              break;
            }
          }
          return;
        }
      }
      await self.clients.openWindow(target.href);
    })()
  );
});
