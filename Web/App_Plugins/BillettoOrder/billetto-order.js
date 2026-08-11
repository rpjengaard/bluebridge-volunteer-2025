// [CHANGE: Billetto ordre property editor] Related: Web/Controllers/BillettoOrderController.cs, Code/Services/BillettoTicketService.cs, Web/App_Plugins/BillettoOrder/umbraco-package.json
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import { UMB_PROPERTY_DATASET_CONTEXT } from "@umbraco-cms/backoffice/property";

const MATCHED_BY_LABELS = {
  billettoId: "Billetto Id",
  altEmail: "alternativ Billetto e-mail",
  email: "e-mail",
};

export default class BillettoOrderPropertyEditorElement extends UmbElementMixin(LitElement) {
  static properties = {
    value: { type: String },
    _order: { state: true },
    _matchedBy: { state: true },
    _billettoId: { state: true },
    _loading: { state: true },
    _error: { state: true },
    _message: { state: true },
    _contextReady: { state: true },
  };

  #authContext;
  #memberKey;
  #billettoId;
  #altEmail;
  #initialLoadDone = false;

  constructor() {
    super();
    this._order = null;
    this._matchedBy = null;
    this._billettoId = null;
    this._loading = false;
    this._error = null;
    this._message = null;
    this._contextReady = false;

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });

    this.consumeContext(UMB_PROPERTY_DATASET_CONTEXT, async (datasetContext) => {
      if (!datasetContext) return;
      this.#memberKey = datasetContext.getUnique();
      this._contextReady = true;

      this.observe(await datasetContext.propertyValueByAlias("billettoId"), (value) => {
        this.#billettoId = value || "";
      });
      this.observe(await datasetContext.propertyValueByAlias("altBillettoEmail"), (value) => {
        this.#altEmail = value || "";
      });

      if (!this.#initialLoadDone) {
        this.#initialLoadDone = true;
        this.#loadOrder(false);
      }
    });
  }

  async #loadOrder(refresh) {
    if (!this.#memberKey) return;
    if (!this.#authContext) {
      setTimeout(() => this.#loadOrder(refresh), 100);
      return;
    }

    this._loading = true;
    this._error = null;
    this._message = null;

    try {
      const params = new URLSearchParams({ memberKey: this.#memberKey, refresh });
      if (this.#billettoId) params.set("billettoId", this.#billettoId);
      if (this.#altEmail) params.set("altEmail", this.#altEmail);

      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();
      const res = await fetch(`/umbraco/management/api/v1/billettoorder/order?${params}`, {
        method: "GET",
        credentials: config.credentials,
        headers: { Authorization: `Bearer ${authToken}` },
      });

      const data = await res.json();
      if (!res.ok || !data.success) {
        this._error = data.message || `Serverfejl: ${res.status}`;
        this._order = null;
        this._matchedBy = null;
        this._billettoId = null;
      } else {
        this._order = data.order;
        this._matchedBy = data.matchedBy;
        this._billettoId = data.billettoId;
        this._message = data.found ? null : data.message;
      }
    } catch (error) {
      this._error = error.message || "Kunne ikke hente data";
    } finally {
      this._loading = false;
    }
  }

  render() {
    if (!this._contextReady) {
      return "";
    }
    if (!this.#memberKey) {
      return html`<div class="notice">Gem medlemmet først for at kunne slå Billetto-ordren op.</div>`;
    }

    return html`
      ${this._error ? html`<div class="alert alert-danger"><strong>${this._error}</strong></div>` : ""}
      ${this._loading ? this.#renderLoading() : this.#renderResult()}
      <div class="toolbar">
        <uui-button
          look="secondary"
          compact
          ?disabled="${this._loading}"
          @click="${() => this.#loadOrder(true)}">
          ${this._loading ? "Opdaterer..." : "Opdatér fra Billetto"}
        </uui-button>
      </div>
    `;
  }

  #renderLoading() {
    return html`
      <div class="loading">
        <uui-loader-bar></uui-loader-bar>
        <div class="loading-title">
          Henter ordre fra Billetto... (kan tage flere minutter første gang, hvis der slås op via e-mail)
        </div>
      </div>
    `;
  }

  #renderResult() {
    if (this._message) {
      return html`<div class="notice">${this._message}</div>`;
    }
    if (!this._matchedBy && !this._order) {
      return "";
    }

    return html`
      <div class="matched-by">
        Fundet via: <span class="badge">${MATCHED_BY_LABELS[this._matchedBy] || this._matchedBy}</span>
        ${this._billettoId ? html`<span class="order-id">Ordre-id: ${this._billettoId}</span>` : ""}
      </div>
      ${this._order
        ? this.#renderEntries(this._order)
        : html`<div class="notice">Ordren kunne ikke hentes fra Billetto lige nu.</div>`}
    `;
  }

  #renderEntries(obj) {
    return html`
      <table class="order-table">
        <tbody>
          ${Object.entries(obj).map(
            ([key, value]) => html`
              <tr>
                <th>${key}</th>
                <td>${this.#renderValue(value)}</td>
              </tr>
            `
          )}
        </tbody>
      </table>
    `;
  }

  #renderValue(value) {
    if (value === null || value === undefined || value === "") {
      return html`<span class="text-muted">-</span>`;
    }
    if (Array.isArray(value)) {
      if (value.length === 0) return html`<span class="text-muted">-</span>`;
      return html`<ul class="value-list">
        ${value.map((item) => html`<li>${this.#renderValue(item)}</li>`)}
      </ul>`;
    }
    if (typeof value === "object") {
      return this.#renderEntries(value);
    }
    return String(value);
  }

  static styles = css`
    :host { display: block; }

    .toolbar { margin-top: 12px; }

    .matched-by {
      margin-bottom: 10px;
      font-size: 0.9em;
      color: var(--uui-color-text-alt);
    }
    .badge {
      display: inline-block; padding: 2px 7px; border-radius: 4px;
      font-size: 0.9em; font-weight: 500;
      background: #4caf50; color: white;
    }
    .order-id { margin-left: 10px; }

    .order-table {
      width: 100%; border-collapse: collapse;
      background: var(--uui-color-surface);
    }
    .order-table th, .order-table td {
      padding: 6px 10px; text-align: left; vertical-align: top;
      border-bottom: 1px solid var(--uui-color-border);
      font-size: 0.9em;
    }
    .order-table th {
      width: 180px;
      background: var(--uui-color-surface-alt);
      font-weight: 600;
      word-break: break-word;
    }
    .order-table td { word-break: break-word; }
    .order-table .order-table th { width: 140px; }

    .value-list { margin: 0; padding-left: 18px; }

    .alert {
      padding: 14px 16px; border-radius: 6px; margin-bottom: 12px;
    }
    .alert-danger { background: rgba(244, 67, 54, 0.1); color: #c62828; border: 1px solid #f44336; }

    .notice {
      padding: 10px 0;
      color: var(--uui-color-text-alt);
    }

    .loading {
      padding: 20px 0;
      color: var(--uui-color-text-alt);
    }
    .loading-title { margin-top: 10px; font-size: 0.9em; }

    .text-muted { color: var(--uui-color-text-alt); }
  `;
}

customElements.define("billetto-order-property-editor", BillettoOrderPropertyEditorElement);
