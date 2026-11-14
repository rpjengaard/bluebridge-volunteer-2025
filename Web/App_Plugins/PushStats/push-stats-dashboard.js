// [CHANGE: web push notifications — backoffice stats dashboard] Related: Web/Controllers/PushStatsController.cs, Code/Services/PushNotificationService.cs, Web/App_Plugins/PushStats/umbraco-package.json
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

const API = "/umbraco/management/api/v1/pushstats";

export default class PushStatsDashboardElement extends UmbElementMixin(LitElement) {
  static properties = {
    _data: { state: true },
    _loading: { state: true },
    _error: { state: true },
    _keys: { state: true },
    _sort: { state: true },
  };

  #authContext;

  constructor() {
    super();
    this._data = null;
    this._loading = false;
    this._error = null;
    this._keys = null;
    this._sort = { column: "name", asc: true };

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });
  }

  connectedCallback() {
    super.connectedCallback();
    this.#loadData();
  }

  async #request(path, method = "GET") {
    const config = this.#authContext.getOpenApiConfiguration();
    const authToken = await config.token();
    const res = await fetch(`${API}/${path}`, {
      method,
      credentials: config.credentials,
      headers: { Authorization: `Bearer ${authToken}` },
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || data.success === false) {
      throw new Error(data.message || `Serverfejl: ${res.status}`);
    }
    return data;
  }

  async #loadData() {
    if (!this.#authContext) {
      setTimeout(() => this.#loadData(), 100);
      return;
    }

    this._loading = true;
    this._error = null;
    try {
      this._data = await this.#request("summary");
    } catch (error) {
      this._error = error.message || "Kunne ikke hente data";
    } finally {
      this._loading = false;
    }
  }

  async #generateKeys() {
    try {
      this._keys = await this.#request("generate-keys", "POST");
    } catch (error) {
      this._error = error.message;
    }
  }

  #percent(part, whole) {
    if (!whole) return "–";
    return `${Math.round((part / whole) * 100)}%`;
  }

  #setSort(column) {
    this._sort = this._sort.column === column
      ? { column, asc: !this._sort.asc }
      : { column, asc: column === "name" };
  }

  #sortedCrews() {
    const { column, asc } = this._sort;
    const value = (c) => column === "percent"
      ? (c.memberCount ? c.subscribedCount / c.memberCount : -1)
      : c[column];
    return [...(this._data?.crews ?? [])].sort((a, b) => {
      const va = value(a), vb = value(b);
      const cmp = typeof va === "string" ? va.localeCompare(vb, "da") : va - vb;
      return asc ? cmp : -cmp;
    });
  }

  #formatDateTime(dateString) {
    if (!dateString) return "–";
    return new Date(dateString.endsWith("Z") ? dateString : `${dateString}Z`).toLocaleString("da-DK", {
      day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
    });
  }

  render() {
    return html`
      <uui-box headline="Push-notifikationer">
        <div class="content">
          ${this._error ? html`<div class="alert alert-danger"><strong>${this._error}</strong></div>` : ""}
          ${this._loading && !this._data ? html`
            <div class="loading"><uui-loader-bar></uui-loader-bar><div>Henter push-statistik...</div></div>
          ` : ""}
          ${this._data ? html`
            ${!this._data.configured ? this.#renderSetup() : ""}
            ${this.#renderStats()}
          ` : ""}
        </div>
      </uui-box>
    `;
  }

  #renderSetup() {
    return html`
      <div class="alert alert-warning">
        <strong>Push er ikke sat op.</strong> Der mangler VAPID-nøgler i konfigurationen, så frivillige kan ikke
        slå notifikationer til, og der sendes ingen push.
        <p>Tilføj en <code>WebPush</code>-sektion i <code>appsettings.json</code> (den private nøgle bør ligge i
        environment variables / user secrets — ikke i git) og genstart sitet:</p>
        <pre>"WebPush": {
  "Subject": "mailto:kontakt@eksempel.dk",
  "PublicKey": "${this._keys?.publicKey ?? "..."}",
  "PrivateKey": "${this._keys?.privateKey ?? "..."}"
}</pre>
        ${this._data.isAdmin && !this._keys ? html`
          <uui-button look="primary" @click="${() => this.#generateKeys()}">Generér nøglepar</uui-button>
        ` : ""}
        ${this._keys ? html`
          <p><strong>Gem nøglerne nu.</strong> De gemmes ikke af Umbraco og vises kun denne ene gang.
          Skifter I nøgler senere, skal alle slå notifikationer til igen.</p>
        ` : ""}
      </div>
    `;
  }

  #renderStats() {
    const d = this._data;
    const crews = this.#sortedCrews();
    const header = (column, label, numeric = true) => html`
      <th class="${numeric ? "num-cell" : ""} sortable" @click="${() => this.#setSort(column)}">
        ${label}${this._sort.column === column ? (this._sort.asc ? " ▲" : " ▼") : ""}
      </th>
    `;

    return html`
      <div class="stats-row">
        <div class="stat-box stat-primary">
          <div class="stat-number">${d.totalSubscribedMembers}</div>
          <div class="stat-label">Frivillige med push</div>
        </div>
        <div class="stat-box stat-default">
          <div class="stat-number">${d.totalDevices}</div>
          <div class="stat-label">Enheder i alt</div>
        </div>
        <div class="stat-box stat-success">
          <div class="stat-number">${d.devicesIos}</div>
          <div class="stat-label">iPhone / iPad</div>
        </div>
        <div class="stat-box stat-success">
          <div class="stat-number">${d.devicesAndroid}</div>
          <div class="stat-label">Android</div>
        </div>
        <div class="stat-box stat-success">
          <div class="stat-number">${d.devicesDesktop}</div>
          <div class="stat-label">Computer</div>
        </div>
      </div>

      <div class="toolbar">
        <span class="muted">Seneste tilmelding: ${this.#formatDateTime(d.latestSubscriptionUtc)}</span>
        <uui-button look="secondary" compact ?disabled="${this._loading}" @click="${() => this.#loadData()}">
          ${this._loading ? "Opdaterer..." : "Opdatér"}
        </uui-button>
      </div>

      <div class="table-wrap">
        <table class="stats-table">
          <thead>
            <tr>
              ${header("name", "Crew", false)}
              ${header("memberCount", "Medlemmer")}
              ${header("subscribedCount", "Med push")}
              ${header("percent", "%")}
              ${header("deviceCount", "Enheder")}
            </tr>
          </thead>
          <tbody>
            ${crews.length === 0
              ? html`<tr><td colspan="5" class="empty-row">Ingen crews fundet</td></tr>`
              : crews.map((c) => html`
                <tr>
                  <td>${c.name}</td>
                  <td class="num-cell">${c.memberCount}</td>
                  <td class="num-cell">${c.subscribedCount}</td>
                  <td class="num-cell">
                    <div class="bar-cell">
                      <div class="bar"><div class="bar-fill" style="width:${c.memberCount ? Math.round((c.subscribedCount / c.memberCount) * 100) : 0}%"></div></div>
                      <span>${this.#percent(c.subscribedCount, c.memberCount)}</span>
                    </div>
                  </td>
                  <td class="num-cell">${c.deviceCount}</td>
                </tr>
              `)}
          </tbody>
        </table>
      </div>
      <p class="muted footnote">
        Medlemmer = frivillige tildelt crewet (ekskl. afviste og afmeldte). Med push = medlemmer med mindst én enhed,
        der har notifikationer slået til. En frivillig kan være på flere crews.
      </p>
    `;
  }

  static styles = css`
    :host { display: block; padding: 20px; }
    .content { padding: 10px 0; }

    .stats-row { display: flex; gap: 12px; margin-bottom: 20px; flex-wrap: wrap; }
    .stat-box {
      flex: 1; min-width: 120px; padding: 14px;
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 6px; text-align: center;
    }
    .stat-box.stat-primary { background: rgba(59, 130, 246, 0.1); border-left: 4px solid #3b82f6; }
    .stat-box.stat-success { background: rgba(76, 175, 80, 0.1); border-left: 4px solid #4caf50; }
    .stat-box.stat-default { background: rgba(158, 158, 158, 0.1); border-left: 4px solid #9e9e9e; }
    .stat-number { font-size: 2em; font-weight: bold; color: var(--uui-color-text); }
    .stat-label { font-size: 0.85em; color: var(--uui-color-text-alt); margin-top: 4px; }

    .toolbar {
      display: flex; justify-content: space-between; align-items: center;
      gap: 12px; flex-wrap: wrap; margin-bottom: 16px;
    }
    .muted { font-size: 0.85em; color: var(--uui-color-text-alt); }
    .footnote { margin-top: 12px; }

    .table-wrap { overflow-x: auto; }
    .stats-table { width: 100%; border-collapse: collapse; }
    .stats-table th, .stats-table td {
      padding: 10px 12px; text-align: left;
      border-bottom: 1px solid var(--uui-color-border);
      white-space: nowrap;
    }
    .stats-table th { background: var(--uui-color-surface-alt); font-weight: 600; font-size: 0.9em; }
    .stats-table th.sortable { cursor: pointer; user-select: none; }
    .stats-table tbody tr:hover { background: var(--uui-color-surface-alt); }
    .num-cell { text-align: right; }

    .bar-cell { display: flex; align-items: center; justify-content: flex-end; gap: 8px; }
    .bar { width: 80px; height: 8px; border-radius: 4px; background: var(--uui-color-border); overflow: hidden; }
    .bar-fill { height: 100%; background: #4caf50; }

    .alert { padding: 14px 16px; border-radius: 6px; margin-bottom: 20px; }
    .alert p { margin: 10px 0; }
    .alert pre {
      background: var(--uui-color-surface); padding: 10px; border-radius: 4px;
      overflow-x: auto; user-select: all; font-size: 0.85em;
    }
    .alert-danger { background: rgba(244, 67, 54, 0.1); color: #c62828; border: 1px solid #f44336; }
    .alert-warning { background: rgba(255, 152, 0, 0.12); color: #7a4800; border: 1px solid #ff9800; }

    .loading, .empty-row { text-align: center; padding: 40px; color: var(--uui-color-text-alt); }
  `;
}

customElements.define("push-stats-dashboard", PushStatsDashboardElement);
