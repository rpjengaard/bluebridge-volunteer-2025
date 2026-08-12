// [CHANGE: Billetto sales dashboard] Related: Web/Controllers/BillettoSalesController.cs, Code/Services/BillettoSalesService.cs, Web/App_Plugins/BillettoSales/umbraco-package.json
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

export default class BillettoSalesDashboardElement extends UmbElementMixin(LitElement) {
  static properties = {
    _data: { state: true },
    _loading: { state: true },
    _error: { state: true },
    _progress: { state: true },
  };

  #authContext;
  #progressTimer;

  constructor() {
    super();
    this._data = null;
    this._loading = false;
    this._error = null;
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
      const res = await fetch(`/umbraco/management/api/v1/billettosales/summary?refresh=${refresh}`, {
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
      const res = await fetch("/umbraco/management/api/v1/billettosales/progress", {
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

  #formatDateTime(dateString) {
    if (!dateString) return "-";
    return new Date(dateString).toLocaleDateString("da-DK", {
      day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
    });
  }

  #percent(part, whole) {
    if (!whole) return "-";
    return `${Math.round((part / whole) * 100)}%`;
  }

  render() {
    return html`
      <uui-box headline="Billetto: Billetsalg">
        <div class="content">
          ${this._error ? html`
            <div class="alert alert-danger"><strong>${this._error}</strong></div>
          ` : ""}

          ${this._loading ? this.#renderLoading() : ""}
          ${!this._loading && this._data && this._data.configured !== false
            ? this.#renderSales()
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
        <div class="loading-title">Henter billetsalg fra Billetto...</div>
        ${p?.active ? html`
          <div class="progress-details">
            <span>Side ${p.pagesFetched} · ${p.attendeesFetched} billetter hentet</span>
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

  #renderSales() {
    const d = this._data;
    const hasCheckIn = d.checkInDataAvailable;

    return html`
      <div class="stats-row">
        <div class="stat-box stat-primary">
          <div class="stat-number">${d.totalSold}</div>
          <div class="stat-label">Solgte billetter</div>
        </div>
        <div class="stat-box stat-success">
          <div class="stat-number">${hasCheckIn ? d.totalCheckedIn : "–"}</div>
          <div class="stat-label">
            Checket ind${hasCheckIn ? ` (${this.#percent(d.totalCheckedIn, d.totalSold)})` : ""}
          </div>
        </div>
        ${d.cancelledCount > 0 ? html`
          <div class="stat-box stat-default">
            <div class="stat-number">${d.cancelledCount}</div>
            <div class="stat-label">Annullerede</div>
          </div>
        ` : ""}
      </div>

      ${!hasCheckIn ? html`
        <div class="alert alert-warning">
          Check-in-data er ikke tilgængelig fra Billetto endnu.
        </div>
      ` : ""}

      <div class="toolbar">
        <span class="fetched-at">Billetto-data hentet: ${this.#formatDateTime(d.fetchedAt)}</span>
        <uui-button look="secondary" compact
          ?disabled="${this._loading}"
          @click="${() => this.#loadData(true)}">
          ${this._loading ? "Opdaterer..." : "Opdatér fra Billetto"}
        </uui-button>
      </div>

      <div class="table-wrap">
        <table class="sales-table">
          <thead>
            <tr>
              <th>Billettype</th>
              <th class="num-cell">Solgt</th>
              <th class="num-cell">Checket ind</th>
              <th class="num-cell">%</th>
            </tr>
          </thead>
          <tbody>
            ${!d.ticketTypes || d.ticketTypes.length === 0
              ? html`<tr><td colspan="4" class="empty-row">Ingen billetter solgt endnu</td></tr>`
              : d.ticketTypes.map((t) => html`
                <tr>
                  <td>${t.name}</td>
                  <td class="num-cell">${t.sold}</td>
                  <td class="num-cell">${hasCheckIn ? t.checkedIn : "–"}</td>
                  <td class="num-cell">${hasCheckIn ? this.#percent(t.checkedIn, t.sold) : "–"}</td>
                </tr>
              `)}
          </tbody>
          ${d.ticketTypes && d.ticketTypes.length > 0 ? html`
            <tfoot>
              <tr>
                <td>I alt</td>
                <td class="num-cell">${d.totalSold}</td>
                <td class="num-cell">${hasCheckIn ? d.totalCheckedIn : "–"}</td>
                <td class="num-cell">${hasCheckIn ? this.#percent(d.totalCheckedIn, d.totalSold) : "–"}</td>
              </tr>
            </tfoot>
          ` : ""}
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
    .stat-box.stat-primary { background: rgba(59, 130, 246, 0.1); border-left: 4px solid #3b82f6; }
    .stat-box.stat-success { background: rgba(76, 175, 80, 0.1); border-left: 4px solid #4caf50; }
    .stat-box.stat-default { background: rgba(158, 158, 158, 0.1); border-left: 4px solid #9e9e9e; }
    .stat-number { font-size: 2em; font-weight: bold; color: var(--uui-color-text); }
    .stat-label { font-size: 0.85em; color: var(--uui-color-text-alt); margin-top: 4px; }

    .toolbar {
      display: flex; justify-content: space-between; align-items: center;
      gap: 12px; flex-wrap: wrap; margin-bottom: 16px;
    }
    .fetched-at { font-size: 0.85em; color: var(--uui-color-text-alt); }

    .table-wrap { overflow-x: auto; }
    .sales-table { width: 100%; border-collapse: collapse; }
    .sales-table th, .sales-table td {
      padding: 10px 12px; text-align: left;
      border-bottom: 1px solid var(--uui-color-border);
      white-space: nowrap;
    }
    .sales-table th {
      background: var(--uui-color-surface-alt);
      font-weight: 600; font-size: 0.9em;
    }
    .sales-table tbody tr:hover { background: var(--uui-color-surface-alt); }
    .sales-table tfoot td {
      font-weight: 600;
      border-top: 2px solid var(--uui-color-border-standalone, var(--uui-color-border));
      border-bottom: none;
    }
    .num-cell { text-align: right; }

    .alert {
      padding: 14px 16px; border-radius: 6px; margin-bottom: 20px;
    }
    .alert-danger { background: rgba(244, 67, 54, 0.1); color: #c62828; border: 1px solid #f44336; }
    .alert-warning { background: rgba(255, 152, 0, 0.12); color: #b26a00; border: 1px solid #ff9800; }

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
  `;
}

customElements.define("billetto-sales-dashboard", BillettoSalesDashboardElement);
