// [CHANGE: Billetto ticket status dashboard] Related: Web/Controllers/BillettoTicketStatusController.cs, Code/Services/BillettoTicketService.cs, Web/App_Plugins/BillettoTicketStatus/umbraco-package.json
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

export default class BillettoTicketStatusElement extends UmbElementMixin(LitElement) {
  static properties = {
    _data: { state: true },
    _loading: { state: true },
    _error: { state: true },
    _search: { state: true },
    _copied: { state: true },
    _progress: { state: true },
  };

  #authContext;
  #progressTimer;

  constructor() {
    super();
    this._data = null;
    this._loading = false;
    this._error = null;
    this._search = "";
    this._copied = false;
    this._progress = null;

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });
  }

  connectedCallback() {
    super.connectedCallback();
    this.#loadData(false);
  }

  async #loadData(refresh) {
    if (!this.#authContext) {
      setTimeout(() => this.#loadData(refresh), 100);
      return;
    }

    this._loading = true;
    this._error = null;
    this.#startProgressPolling();

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();
      const res = await fetch(`/umbraco/management/api/v1/billettoticketstatus/status?refresh=${refresh}`, {
        method: "GET",
        credentials: config.credentials,
        headers: { Authorization: `Bearer ${authToken}` },
      });

      const data = await res.json();
      if (!res.ok || !data.success) {
        this._error = data.message || `Serverfejl: ${res.status}`;
        this._data = data.configured === false ? data : this._data;
      } else {
        this._data = data;
      }
    } catch (error) {
      this._error = error.message || "Kunne ikke hente data";
    } finally {
      this._loading = false;
      this.#stopProgressPolling();
    }
  }

  #startProgressPolling() {
    this.#stopProgressPolling();
    this.#progressTimer = setInterval(() => this.#pollProgress(), 1000);
  }

  #stopProgressPolling() {
    if (this.#progressTimer) {
      clearInterval(this.#progressTimer);
      this.#progressTimer = null;
    }
    this._progress = null;
  }

  async #pollProgress() {
    if (!this.#authContext) return;
    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();
      const res = await fetch("/umbraco/management/api/v1/billettoticketstatus/progress", {
        method: "GET",
        credentials: config.credentials,
        headers: { Authorization: `Bearer ${authToken}` },
      });
      if (res.ok) {
        this._progress = await res.json();
      }
    } catch {
      // ignore polling errors
    }
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.#stopProgressPolling();
  }

  #getFilteredMembers() {
    if (!this._data?.missingMembers) return [];
    const q = this._search.toLowerCase().trim();
    if (!q) return this._data.missingMembers;
    return this._data.missingMembers.filter(
      (m) =>
        (m.fullName && m.fullName.toLowerCase().includes(q)) ||
        (m.email && m.email.toLowerCase().includes(q)) ||
        (m.crewNames && m.crewNames.some((c) => c.toLowerCase().includes(q)))
    );
  }

  async #copyEmails() {
    const emails = this.#getFilteredMembers()
      .map((m) => m.email)
      .filter(Boolean)
      .join("; ");
    try {
      await navigator.clipboard.writeText(emails);
      this._copied = true;
      setTimeout(() => { this._copied = false; }, 2000);
    } catch {
      prompt("Kopiér emails:", emails);
    }
  }

  #formatDateTime(dateString) {
    if (!dateString) return "-";
    return new Date(dateString).toLocaleDateString("da-DK", {
      day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
    });
  }

  render() {
    return html`
      <uui-box headline="Billetto: Hvem mangler billet?">
        <div class="content">
          ${this._error ? html`
            <div class="alert alert-danger"><strong>${this._error}</strong></div>
          ` : ""}

          ${this._loading ? this.#renderLoading() : ""}
          ${!this._loading && this._data && this._data.configured !== false
            ? this.#renderStatus()
            : ""}
        </div>
      </uui-box>
    `;
  }

  #renderLoading() {
    const p = this._progress;
    return html`
      <div class="loading">
        <uui-loader-bar></uui-loader-bar>
        <div class="loading-title">Henter ordrer fra Billetto...</div>
        ${p?.active ? html`
          <div class="progress-details">
            <span>Side ${p.pagesFetched} · ${p.attendeesFetched} ordrer hentet</span>
            ${p.ratelimitRemaining != null ? html`
              <span>· Rate limit: ${p.ratelimitRemaining}${p.ratelimitLimit ? ` / ${p.ratelimitLimit}` : ""} tilbage</span>
            ` : ""}
            ${p.throttledWaitSeconds != null ? html`
              <div class="throttle-notice">⏳ Billetto throttler — venter ${Math.round(p.throttledWaitSeconds)} sekunder...</div>
            ` : ""}
          </div>
        ` : html`<div class="progress-details">Bruger evt. cachede data...</div>`}
      </div>
    `;
  }

  #renderStatus() {
    const d = this._data;
    const filtered = this.#getFilteredMembers();

    return html`
      <div class="stats-row">
        <div class="stat-box">
          <div class="stat-number">${d.totalChecked}</div>
          <div class="stat-label">Tjekkede frivillige</div>
        </div>
        <div class="stat-box stat-success">
          <div class="stat-number">${d.withTicket}</div>
          <div class="stat-label">Har billet (ordre fundet)</div>
        </div>
        <div class="stat-box stat-danger">
          <div class="stat-number">${d.missingCount}</div>
          <div class="stat-label">Mangler billet</div>
        </div>
        <div class="stat-box stat-default">
          <div class="stat-number">${d.exemptCount}</div>
          <div class="stat-label">Fritaget (skal ikke have billet)</div>
        </div>
      </div>

      <div class="toolbar">
        <input type="text" class="search-input" placeholder="Søg navn, email eller crew..."
          .value="${this._search}"
          @input="${(e) => { this._search = e.target.value; }}" />
        <div class="toolbar-right">
          <span class="fetched-at">Billetto-data hentet: ${this.#formatDateTime(d.fetchedAt)}</span>
          <uui-button look="secondary" compact
            ?disabled="${filtered.length === 0}"
            @click="${this.#copyEmails}">
            ${this._copied ? "Kopieret!" : `Kopiér ${filtered.length} emails`}
          </uui-button>
          <uui-button look="secondary" compact
            ?disabled="${this._loading}"
            @click="${() => this.#loadData(true)}">
            ${this._loading ? "Opdaterer..." : "Opdatér fra Billetto"}
          </uui-button>
        </div>
      </div>

      <div class="table-wrap">
        <table class="member-table">
          <thead>
            <tr>
              <th>Navn</th>
              <th>Email</th>
              <th>Crew(s)</th>
            </tr>
          </thead>
          <tbody>
            ${filtered.length === 0
              ? html`<tr><td colspan="3" class="empty-row">
                  ${d.missingCount === 0 ? "Alle frivillige har billet 🎉" : "Ingen match på søgningen"}
                </td></tr>`
              : filtered.map((m) => html`
                <tr>
                  <td>
                    <a href="/umbraco/section/member-management/workspace/member/edit/${m.memberKey}"
                      class="member-link" title="Rediger medlem">${m.fullName}</a>
                  </td>
                  <td>
                    ${m.email || html`<span class="text-muted">ingen email</span>`}
                    ${m.usesAltEmail ? html`<span class="badge badge-alt" title="Bruger alternativ Billetto-email">alt</span>` : ""}
                  </td>
                  <td class="crew-cell">
                    ${m.crewNames && m.crewNames.length > 0
                      ? m.crewNames.join(", ")
                      : html`<span class="text-muted">-</span>`}
                  </td>
                </tr>
              `)}
          </tbody>
        </table>
      </div>
    `;
  }

  static styles = css`
    :host { display: block; padding: 20px; }
    .content { padding: 10px 0; }

    .stats-row { display: flex; gap: 12px; margin-bottom: 20px; flex-wrap: wrap; }
    .stat-box {
      flex: 1; min-width: 130px; padding: 14px;
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 6px; text-align: center;
    }
    .stat-box.stat-success { background: rgba(76, 175, 80, 0.1); border-left: 4px solid #4caf50; }
    .stat-box.stat-danger  { background: rgba(244, 67, 54, 0.1); border-left: 4px solid #f44336; }
    .stat-box.stat-default { background: rgba(158, 158, 158, 0.1); border-left: 4px solid #9e9e9e; }
    .stat-number { font-size: 2em; font-weight: bold; color: var(--uui-color-text); }
    .stat-label { font-size: 0.85em; color: var(--uui-color-text-alt); margin-top: 4px; }

    .toolbar {
      display: flex; justify-content: space-between; align-items: center;
      gap: 12px; flex-wrap: wrap; margin-bottom: 16px;
    }
    .toolbar-right { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .fetched-at { font-size: 0.85em; color: var(--uui-color-text-alt); }

    .search-input {
      padding: 7px 10px; border-radius: 4px;
      border: 1px solid var(--uui-color-border);
      background: var(--uui-color-surface);
      font-size: 14px; min-width: 240px;
    }
    .search-input:focus { outline: none; border-color: var(--uui-color-focus); }

    .table-wrap { overflow-x: auto; }
    .member-table { width: 100%; border-collapse: collapse; }
    .member-table th, .member-table td {
      padding: 10px 12px; text-align: left;
      border-bottom: 1px solid var(--uui-color-border);
      white-space: nowrap;
    }
    .member-table th {
      background: var(--uui-color-surface-alt);
      font-weight: 600; font-size: 0.9em;
    }
    .member-table tbody tr:hover { background: var(--uui-color-surface-alt); }

    .crew-cell { max-width: 260px; overflow: hidden; text-overflow: ellipsis; }

    .badge {
      display: inline-block; padding: 2px 7px; border-radius: 4px;
      font-size: 0.78em; font-weight: 500; margin-left: 6px;
    }
    .badge-alt { background: #3b82f6; color: white; }

    .alert {
      padding: 14px 16px; border-radius: 6px; margin-bottom: 20px;
    }
    .alert-danger { background: rgba(244, 67, 54, 0.1); color: #c62828; border: 1px solid #f44336; }

    .loading, .empty-row {
      text-align: center; padding: 40px;
      color: var(--uui-color-text-alt);
    }
    .loading-title { margin-top: 14px; font-weight: 500; }
    .progress-details {
      margin-top: 8px; font-size: 0.9em;
      color: var(--uui-color-text-alt);
    }
    .throttle-notice {
      margin-top: 10px; padding: 8px 12px;
      display: inline-block;
      background: rgba(255, 152, 0, 0.12);
      border: 1px solid #ff9800; border-radius: 4px;
      color: #b26a00;
    }
    .empty-row { padding: 40px !important; }
    .text-muted { color: var(--uui-color-text-alt); }

    .member-link {
      color: var(--uui-color-interactive);
      text-decoration: none; font-weight: 500;
    }
    .member-link:hover {
      color: var(--uui-color-interactive-emphasis);
      text-decoration: underline;
    }
  `;
}

customElements.define("billetto-ticket-status", BillettoTicketStatusElement);
