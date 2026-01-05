import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

export default class JobApplicationsDashboardElement extends UmbElementMixin(LitElement) {
  static properties = {
    _data: { state: true },
    _loading: { state: true },
    _activeTab: { state: true },
    _selectedApplication: { state: true },
    _reviewing: { state: true },
    _result: { state: true },
  };

  #authContext;

  constructor() {
    super();
    this._data = null;
    this._loading = false;
    this._activeTab = "pending";
    this._selectedApplication = null;
    this._reviewing = false;
    this._result = null;

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });

    this.#loadApplications();
  }

  async #loadApplications() {
    if (!this.#authContext) return;

    this._loading = true;
    this._result = null;

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const response = await fetch("/umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetApplicationsForReview", {
        method: "GET",
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
          "Content-Type": "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`Failed to load applications: ${response.status}`);
      }

      const data = await response.json();
      this._data = data;
      this._loading = false;
    } catch (error) {
      this._loading = false;
      this._result = {
        success: false,
        message: error instanceof Error ? error.message : "Failed to load applications",
      };
    }
  }

  #setActiveTab(tab) {
    this._activeTab = tab;
  }

  #selectApplication(application) {
    this._selectedApplication = application;
  }

  async #reviewApplication(status, ticketLink = "", adminNotes = "") {
    if (!this._selectedApplication || !this.#authContext) return;

    this._reviewing = true;
    this._result = null;

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const response = await fetch("/umbraco/backoffice/JobApplications/JobApplicationBackoffice/ReviewApplication", {
        method: "POST",
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          applicationId: this._selectedApplication.applicationId,
          newStatus: status,
          ticketLink: ticketLink,
          adminNotes: adminNotes,
        }),
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || `Failed to review application: ${response.status}`);
      }

      const data = await response.json();
      this._reviewing = false;

      if (data.success) {
        this._result = {
          success: true,
          message: `Application ${status === 1 ? "accepted" : "rejected"} successfully!${data.emailSent ? " Email sent to applicant." : ""}`,
        };
        this._selectedApplication = null;
        await this.#loadApplications();
      } else {
        this._result = {
          success: false,
          message: data.errorMessage || "Failed to review application",
        };
      }
    } catch (error) {
      this._reviewing = false;
      this._result = {
        success: false,
        message: error instanceof Error ? error.message : "An error occurred",
      };
    }
  }

  #formatDate(dateString) {
    if (!dateString) return "-";
    const date = new Date(dateString);
    return date.toLocaleDateString("da-DK", {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  }

  #getStatusBadge(status) {
    switch (status) {
      case 0: // Pending
        return html`<span class="badge badge-warning">Afventer</span>`;
      case 1: // Accepted
        return html`<span class="badge badge-success">Godkendt</span>`;
      case 2: // Rejected
        return html`<span class="badge badge-danger">Afvist</span>`;
      case 3: // Withdrawn
        return html`<span class="badge badge-secondary">Trukket tilbage</span>`;
      default:
        return html`<span class="badge">Ukendt</span>`;
    }
  }

  #renderApplicationsList(applications) {
    if (applications.length === 0) {
      return html`
        <div class="empty-state">
          <p>Ingen ansøgninger at vise</p>
        </div>
      `;
    }

    return html`
      <div class="applications-list">
        ${applications.map(
          (app) => html`
            <div
              class="application-card ${this._selectedApplication?.applicationId === app.applicationId ? "selected" : ""}"
              @click="${() => this.#selectApplication(app)}">
              <div class="card-header">
                <div>
                  <h4 class="applicant-name">${app.memberName}</h4>
                  <p class="job-info">${app.jobTitle} @ ${app.crewName}</p>
                </div>
                ${this.#getStatusBadge(app.status)}
              </div>
              <div class="card-body">
                <div class="info-row">
                  <span class="label">Email:</span>
                  <span>${app.memberEmail}</span>
                </div>
                ${app.memberPhone
                  ? html`
                      <div class="info-row">
                        <span class="label">Telefon:</span>
                        <span>${app.memberPhone}</span>
                      </div>
                    `
                  : ""}
                ${app.memberAge
                  ? html`
                      <div class="info-row">
                        <span class="label">Alder:</span>
                        <span>${app.memberAge} år</span>
                      </div>
                    `
                  : ""}
                <div class="info-row">
                  <span class="label">Indsendt:</span>
                  <span>${this.#formatDate(app.submittedDate)}</span>
                </div>
                ${app.applicationMessage
                  ? html`
                      <div class="message-section">
                        <span class="label">Besked:</span>
                        <p class="message-text">${app.applicationMessage}</p>
                      </div>
                    `
                  : ""}
              </div>
              ${this._activeTab === "pending"
                ? html`
                    <div class="card-actions">
                      <uui-button
                        look="primary"
                        color="positive"
                        @click="${(e) => {
                          e.stopPropagation();
                          this.#openAcceptDialog(app);
                        }}">
                        Godkend
                      </uui-button>
                      <uui-button
                        look="secondary"
                        color="danger"
                        @click="${(e) => {
                          e.stopPropagation();
                          this.#reviewApplication(2);
                        }}">
                        Afvis
                      </uui-button>
                    </div>
                  `
                : ""}
              ${app.reviewedDate
                ? html`
                    <div class="review-info">
                      <small>Behandlet: ${this.#formatDate(app.reviewedDate)}</small>
                      ${app.reviewedByName ? html`<small>Af: ${app.reviewedByName}</small>` : ""}
                    </div>
                  `
                : ""}
            </div>
          `
        )}
      </div>
    `;
  }

  #openAcceptDialog(application) {
    const ticketLink = prompt("Indtast billet link (valgfri):", "");
    if (ticketLink !== null) {
      this._selectedApplication = application;
      this.#reviewApplication(1, ticketLink);
    }
  }

  render() {
    return html`
      <uui-box headline="Job Applications Management">
        <div class="content">
          ${this._data?.isAdmin || this._data?.isScheduler
            ? html`
                <div class="info-banner">
                  <svg class="info-icon" fill="currentColor" viewBox="0 0 20 20">
                    <path
                      fill-rule="evenodd"
                      d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z"
                      clip-rule="evenodd" />
                  </svg>
                  <div>
                    <h4>Administrer ansøgninger</h4>
                    <p>
                      ${this._data.isAdmin
                        ? "Du kan se og behandle alle ansøgninger fra frivillige til ledige stillinger."
                        : "Du kan se og behandle ansøgninger til crews du er ansvarlig for."}
                    </p>
                  </div>
                </div>
              `
            : ""}

          ${this._result
            ? html`
                <div class="alert ${this._result.success ? "alert-success" : "alert-danger"}">
                  <strong>${this._result.success ? "Succes!" : "Fejl!"}</strong> ${this._result.message}
                </div>
              `
            : ""}

          ${this._loading
            ? html`<div class="loading">Indlæser ansøgninger...</div>`
            : this._data
              ? html`
                  <div class="stats-grid">
                    <div class="stat-card">
                      <div class="stat-value">${this._data.pendingApplications.length}</div>
                      <div class="stat-label">Afventende</div>
                    </div>
                    <div class="stat-card">
                      <div class="stat-value">${this._data.acceptedApplications.length}</div>
                      <div class="stat-label">Godkendt</div>
                    </div>
                    <div class="stat-card">
                      <div class="stat-value">${this._data.rejectedApplications.length}</div>
                      <div class="stat-label">Afvist</div>
                    </div>
                  </div>

                  <div class="tabs">
                    <button
                      class="tab ${this._activeTab === "pending" ? "active" : ""}"
                      @click="${() => this.#setActiveTab("pending")}">
                      Afventende (${this._data.pendingApplications.length})
                    </button>
                    <button
                      class="tab ${this._activeTab === "accepted" ? "active" : ""}"
                      @click="${() => this.#setActiveTab("accepted")}">
                      Godkendt (${this._data.acceptedApplications.length})
                    </button>
                    <button
                      class="tab ${this._activeTab === "rejected" ? "active" : ""}"
                      @click="${() => this.#setActiveTab("rejected")}">
                      Afvist (${this._data.rejectedApplications.length})
                    </button>
                  </div>

                  <div class="tab-content">
                    ${this._activeTab === "pending"
                      ? this.#renderApplicationsList(this._data.pendingApplications)
                      : this._activeTab === "accepted"
                        ? this.#renderApplicationsList(this._data.acceptedApplications)
                        : this.#renderApplicationsList(this._data.rejectedApplications)}
                  </div>
                `
              : ""}
        </div>
      </uui-box>
    `;
  }

  static styles = css`
    :host {
      display: block;
      padding: 20px;
    }

    .content {
      padding: 10px 0;
    }

    .info-banner {
      display: flex;
      align-items: start;
      gap: 15px;
      padding: 15px;
      background: var(--uui-color-positive-emphasis, #e8f5e9);
      color: #000;
      border-radius: 6px;
      margin-bottom: 20px;
    }

    .info-icon {
      width: 24px;
      height: 24px;
      flex-shrink: 0;
      color: var(--uui-color-positive, #4caf50);
    }

    .info-banner h4 {
      margin: 0 0 5px 0;
      font-size: 16px;
      font-weight: 600;
    }

    .info-banner p {
      margin: 0;
      font-size: 14px;
    }

    .loading {
      text-align: center;
      padding: 40px;
      color: var(--uui-color-text, #333);
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 15px;
      margin-bottom: 20px;
    }

    .stat-card {
      background: white;
      border: 1px solid var(--uui-color-border, #ddd);
      border-radius: 6px;
      padding: 20px;
      text-align: center;
    }

    .stat-value {
      font-size: 32px;
      font-weight: 700;
      color: var(--uui-color-current, #1b264f);
    }

    .stat-label {
      font-size: 14px;
      color: var(--uui-color-text-alt, #666);
      margin-top: 5px;
    }

    .tabs {
      display: flex;
      gap: 10px;
      margin-bottom: 20px;
      border-bottom: 2px solid var(--uui-color-border, #ddd);
    }

    .tab {
      padding: 10px 20px;
      background: none;
      border: none;
      border-bottom: 3px solid transparent;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
      color: var(--uui-color-text-alt, #666);
      transition: all 0.2s;
    }

    .tab:hover {
      color: var(--uui-color-text, #333);
    }

    .tab.active {
      color: var(--uui-color-current, #1b264f);
      border-bottom-color: var(--uui-color-current, #1b264f);
    }

    .tab-content {
      margin-top: 20px;
    }

    .applications-list {
      display: flex;
      flex-direction: column;
      gap: 15px;
    }

    .application-card {
      background: white;
      border: 2px solid var(--uui-color-border, #ddd);
      border-radius: 8px;
      padding: 20px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .application-card:hover {
      border-color: var(--uui-color-current, #1b264f);
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
    }

    .application-card.selected {
      border-color: var(--uui-color-positive, #4caf50);
      background: var(--uui-color-positive-emphasis, #e8f5e9);
    }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: start;
      margin-bottom: 15px;
    }

    .applicant-name {
      margin: 0 0 5px 0;
      font-size: 18px;
      font-weight: 600;
      color: var(--uui-color-text, #333);
    }

    .job-info {
      margin: 0;
      font-size: 14px;
      color: var(--uui-color-text-alt, #666);
    }

    .badge {
      display: inline-block;
      padding: 4px 12px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 600;
    }

    .badge-warning {
      background: #ff9800;
      color: white;
    }

    .badge-success {
      background: var(--uui-color-positive, #4caf50);
      color: white;
    }

    .badge-danger {
      background: var(--uui-color-danger, #f44336);
      color: white;
    }

    .badge-secondary {
      background: #9e9e9e;
      color: white;
    }

    .card-body {
      margin-bottom: 15px;
    }

    .info-row {
      display: flex;
      gap: 10px;
      margin-bottom: 8px;
      font-size: 14px;
    }

    .label {
      font-weight: 600;
      color: var(--uui-color-text, #333);
      min-width: 80px;
    }

    .message-section {
      margin-top: 12px;
      padding-top: 12px;
      border-top: 1px solid var(--uui-color-border, #ddd);
    }

    .message-text {
      margin: 5px 0 0 0;
      padding: 10px;
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 4px;
      font-size: 14px;
      white-space: pre-wrap;
    }

    .card-actions {
      display: flex;
      gap: 10px;
      padding-top: 15px;
      border-top: 1px solid var(--uui-color-border, #ddd);
    }

    .review-info {
      display: flex;
      gap: 15px;
      padding-top: 10px;
      margin-top: 10px;
      border-top: 1px solid var(--uui-color-border, #ddd);
      font-size: 12px;
      color: var(--uui-color-text-alt, #666);
    }

    .empty-state {
      text-align: center;
      padding: 60px 20px;
      color: var(--uui-color-text-alt, #666);
    }

    .alert {
      margin-bottom: 20px;
      padding: 15px;
      border-radius: 6px;
    }

    .alert-success {
      background: var(--uui-color-positive, #4caf50);
      color: white;
    }

    .alert-danger {
      background: var(--uui-color-danger, #f44336);
      color: white;
    }
  `;
}

customElements.define("job-applications-dashboard", JobApplicationsDashboardElement);
