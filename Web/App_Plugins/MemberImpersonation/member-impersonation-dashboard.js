import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

export default class MemberImpersonationDashboardElement extends UmbElementMixin(LitElement) {
  static properties = {
    _members: { state: true },
    _loading: { state: true },
    _searchTerm: { state: true },
    _selectedMember: { state: true },
    _impersonating: { state: true },
    _result: { state: true },
  };

  #authContext;

  constructor() {
    super();
    this._members = [];
    this._loading = false;
    this._searchTerm = "";
    this._selectedMember = null;
    this._impersonating = false;
    this._result = null;

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });

    this.#loadMembers();
  }

  async #loadMembers(search = "") {
    if (!this.#authContext) return;

    this._loading = true;
    this._result = null;

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const url = `/umbraco/management/api/v1/memberimpersonation/members${search ? `?search=${encodeURIComponent(search)}` : ""}`;

      const response = await fetch(url, {
        method: "GET",
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
          "Content-Type": "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`Failed to load members: ${response.status}`);
      }

      const data = await response.json();
      this._members = data.members || [];
      this._loading = false;
    } catch (error) {
      this._loading = false;
      this._result = {
        success: false,
        message: error instanceof Error ? error.message : "Failed to load members",
      };
    }
  }

  #onSearchInput(event) {
    this._searchTerm = event.target.value;
    // Debounce search
    clearTimeout(this._searchTimeout);
    this._searchTimeout = setTimeout(() => {
      this.#loadMembers(this._searchTerm);
    }, 300);
  }

  #selectMember(member) {
    this._selectedMember = member;
  }

  async #startImpersonation() {
    if (!this._selectedMember || !this.#authContext) return;

    this._impersonating = true;
    this._result = null;

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const response = await fetch("/umbraco/management/api/v1/memberimpersonation/start", {
        method: "POST",
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          memberEmail: this._selectedMember.email,
        }),
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Failed to start impersonation: ${response.status} - ${errorText || response.statusText}`);
      }

      const data = await response.json();
      this._impersonating = false;

      if (data.success) {
        this._result = {
          success: true,
          message: `Successfully started impersonating ${this._selectedMember.email}`,
          frontendUrl: data.frontendUrl,
        };
      } else {
        this._result = {
          success: false,
          message: data.message || "Failed to start impersonation",
        };
      }
    } catch (error) {
      this._impersonating = false;
      const errorMessage = error instanceof Error ? error.message : "An error occurred";
      this._result = {
        success: false,
        message: errorMessage,
      };
    }
  }

  #openFrontend() {
    if (this._result?.frontendUrl) {
      window.open(this._result.frontendUrl, "_blank");
    }
  }

  #clearSelection() {
    this._selectedMember = null;
    this._result = null;
  }

  render() {
    return html`
      <uui-box headline="Member Impersonation">
        <div class="content">
          <div class="warning-banner">
            <svg class="warning-icon" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/>
            </svg>
            <div>
              <h4>Admin Support Tool</h4>
              <p>This feature allows you to log in as a member on the frontend to help support them. Use with caution and only for legitimate support purposes.</p>
            </div>
          </div>

          <div class="search-section">
            <uui-label>Search Members</uui-label>
            <input
              type="text"
              class="search-input"
              placeholder="Search by name or email..."
              .value="${this._searchTerm}"
              @input="${this.#onSearchInput}"
            />
          </div>

          ${this._loading
            ? html`<div class="loading">Loading members...</div>`
            : html`
                <div class="members-list">
                  ${this._members.length === 0
                    ? html`<div class="no-results">No members found</div>`
                    : this._members.map(
                        (member) => html`
                          <div
                            class="member-item ${this._selectedMember?.email === member.email ? "selected" : ""}"
                            @click="${() => this.#selectMember(member)}">
                            <div class="member-info">
                              <div class="member-avatar">
                                ${member.firstName?.[0]?.toUpperCase() || member.name?.[0]?.toUpperCase() || "?"}
                              </div>
                              <div class="member-details">
                                <div class="member-name">
                                  ${member.firstName && member.lastName
                                    ? `${member.firstName} ${member.lastName}`
                                    : member.name || "Unnamed"}
                                </div>
                                <div class="member-email">${member.email}</div>
                              </div>
                            </div>
                            ${this._selectedMember?.email === member.email
                              ? html`
                                  <svg class="check-icon" fill="currentColor" viewBox="0 0 20 20">
                                    <path
                                      fill-rule="evenodd"
                                      d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z"
                                      clip-rule="evenodd" />
                                  </svg>
                                `
                              : ""}
                          </div>
                        `
                      )}
                </div>
              `}

          <div class="action-section">
            <uui-button
              look="primary"
              color="positive"
              ?disabled="${!this._selectedMember || this._impersonating}"
              @click="${this.#startImpersonation}">
              ${this._impersonating ? "Starting Impersonation..." : "Start Impersonation"}
            </uui-button>

            <uui-button
              look="secondary"
              ?disabled="${!this._selectedMember || this._impersonating}"
              @click="${this.#clearSelection}">
              Clear Selection
            </uui-button>
          </div>

          ${this._result?.success
            ? html`
                <div class="alert alert-success">
                  <strong>Success!</strong> ${this._result.message}
                  <div class="mt-2">
                    <uui-button look="primary" @click="${this.#openFrontend}">
                      Open Frontend in New Tab
                    </uui-button>
                  </div>
                </div>
              `
            : ""}

          ${this._result && !this._result.success
            ? html`
                <div class="alert alert-danger">
                  <strong>Error!</strong> ${this._result.message}
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

    .warning-banner {
      display: flex;
      align-items: start;
      gap: 15px;
      padding: 15px;
      background: var(--uui-color-warning, #ff9800);
      color: #000;
      border-radius: 6px;
      margin-bottom: 20px;
    }

    .warning-icon {
      width: 24px;
      height: 24px;
      flex-shrink: 0;
    }

    .warning-banner h4 {
      margin: 0 0 5px 0;
      font-size: 16px;
      font-weight: 600;
    }

    .warning-banner p {
      margin: 0;
      font-size: 14px;
    }

    .search-section {
      margin-bottom: 20px;
    }

    .search-section uui-label {
      display: block;
      margin-bottom: 8px;
      font-weight: 600;
    }

    .search-input {
      width: 100%;
      padding: 10px;
      border: 1px solid var(--uui-color-border, #ccc);
      border-radius: 6px;
      font-size: 14px;
      box-sizing: border-box;
    }

    .loading {
      text-align: center;
      padding: 40px;
      color: var(--uui-color-text, #333);
    }

    .no-results {
      text-align: center;
      padding: 40px;
      color: var(--uui-color-text-alt, #666);
    }

    .members-list {
      max-height: 400px;
      overflow-y: auto;
      border: 1px solid var(--uui-color-border, #ccc);
      border-radius: 6px;
      margin-bottom: 20px;
    }

    .member-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px 16px;
      cursor: pointer;
      border-bottom: 1px solid var(--uui-color-border, #eee);
      transition: background 0.2s;
    }

    .member-item:hover {
      background: var(--uui-color-surface-alt, #f5f5f5);
    }

    .member-item.selected {
      background: var(--uui-color-positive-emphasis, #e8f5e9);
      border-left: 3px solid var(--uui-color-positive, #4caf50);
    }

    .member-item:last-child {
      border-bottom: none;
    }

    .member-info {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .member-avatar {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      background: var(--uui-color-current, #1b264f);
      color: white;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 16px;
    }

    .member-details {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .member-name {
      font-weight: 600;
      font-size: 14px;
      color: var(--uui-color-text, #333);
    }

    .member-email {
      font-size: 12px;
      color: var(--uui-color-text-alt, #666);
    }

    .check-icon {
      width: 24px;
      height: 24px;
      color: var(--uui-color-positive, #4caf50);
    }

    .action-section {
      display: flex;
      gap: 10px;
      margin-bottom: 20px;
    }

    .alert {
      margin-top: 20px;
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

    .mt-2 {
      margin-top: 10px;
    }
  `;
}

customElements.define("member-impersonation-dashboard", MemberImpersonationDashboardElement);
