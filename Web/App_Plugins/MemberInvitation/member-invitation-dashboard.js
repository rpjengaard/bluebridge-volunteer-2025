import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

export default class MemberInvitationDashboardElement extends UmbElementMixin(LitElement) {
  static properties = {
    _members: { state: true },
    _loading: { state: true },
    _sending: { state: true },
    _sendingMemberId: { state: true },
    _result: { state: true },
    _filter: { state: true },
    _searchQuery: { state: true },
  };

  #authContext;

  constructor() {
    super();
    this._members = [];
    this._loading = false;
    this._sending = false;
    this._sendingMemberId = null;
    this._result = null;
    this._filter = "all";
    this._searchQuery = "";

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });
  }

  connectedCallback() {
    super.connectedCallback();
    this.#loadMembers();
  }

  async #loadMembers() {
    if (!this.#authContext) {
      setTimeout(() => this.#loadMembers(), 100);
      return;
    }

    this._loading = true;
    this._result = null;

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const response = await fetch("/umbraco/management/api/v1/memberinvitation/members", {
        method: "GET",
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
        },
      });

      if (!response.ok) {
        throw new Error(`Server error: ${response.status}`);
      }

      const data = await response.json();
      this._members = data.members || [];
    } catch (error) {
      this._result = {
        success: false,
        message: error.message || "Failed to load members",
      };
    } finally {
      this._loading = false;
    }
  }

  async #inviteMember(memberId) {
    if (!this.#authContext) return;

    this._sending = true;
    this._sendingMemberId = memberId;
    this._result = null;

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const response = await fetch(`/umbraco/management/api/v1/memberinvitation/invite/${memberId}`, {
        method: "POST",
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
        },
      });

      const data = await response.json();
      this._result = data;

      if (data.success) {
        await this.#loadMembers();
      }
    } catch (error) {
      this._result = {
        success: false,
        message: error.message || "Failed to send invitation",
      };
    } finally {
      this._sending = false;
      this._sendingMemberId = null;
    }
  }

  async #inviteAll() {
    if (!this.#authContext) return;

    if (!confirm("Er du sikker på, at du vil sende invitationer til alle medlemmer?")) {
      return;
    }

    this._sending = true;
    this._result = null;

    try {
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const response = await fetch("/umbraco/management/api/v1/memberinvitation/invite-all", {
        method: "POST",
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
        },
      });

      const data = await response.json();
      this._result = data;

      await this.#loadMembers();
    } catch (error) {
      this._result = {
        success: false,
        message: error.message || "Failed to send bulk invitations",
      };
    } finally {
      this._sending = false;
    }
  }

  #onFilterChange(e) {
    this._filter = e.target.value;
  }

  #onSearchChange(e) {
    this._searchQuery = e.target.value;
  }

  #getFilteredMembers() {
    let filtered = this._members;

    // Apply status filter
    if (this._filter !== "all") {
      filtered = filtered.filter(m => m.status === this._filter);
    }

    // Apply search filter (name and email)
    if (this._searchQuery.trim()) {
      const query = this._searchQuery.toLowerCase().trim();
      filtered = filtered.filter(m =>
        (m.fullName && m.fullName.toLowerCase().includes(query)) ||
        (m.email && m.email.toLowerCase().includes(query))
      );
    }

    return filtered;
  }

  #getStatusBadge(status) {
    switch (status) {
      case "Accepted":
        return html`<span class="badge badge-success">Accepteret</span>`;
      case "Invited":
        return html`<span class="badge badge-warning">Inviteret</span>`;
      default:
        return html`<span class="badge badge-default">Ikke inviteret</span>`;
    }
  }

  #formatDate(dateString) {
    if (!dateString) return "-";
    const date = new Date(dateString);
    return date.toLocaleDateString("da-DK", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  }

  #getCounts() {
    const total = this._members.length;
    const accepted = this._members.filter(m => m.status === "Accepted").length;
    const invited = this._members.filter(m => m.status === "Invited").length;
    const notInvited = this._members.filter(m => m.status === "NotInvited").length;
    return { total, accepted, invited, notInvited };
  }

  render() {
    const counts = this.#getCounts();
    const filteredMembers = this.#getFilteredMembers();

    return html`
      <uui-box headline="Member Invitations - Blue Bridge 2026">
        <div class="content">
          <div class="stats-row">
            <div class="stat-box">
              <div class="stat-number">${counts.total}</div>
              <div class="stat-label">Total medlemmer</div>
            </div>
            <div class="stat-box stat-success">
              <div class="stat-number">${counts.accepted}</div>
              <div class="stat-label">Accepteret</div>
            </div>
            <div class="stat-box stat-warning">
              <div class="stat-number">${counts.invited}</div>
              <div class="stat-label">Inviteret</div>
            </div>
            <div class="stat-box stat-default">
              <div class="stat-number">${counts.notInvited}</div>
              <div class="stat-label">Ikke inviteret</div>
            </div>
          </div>

          <div class="actions-row">
            <div class="filter-controls">
              <div class="search-group">
                <input
                  type="text"
                  placeholder="Søg på navn eller email..."
                  .value="${this._searchQuery}"
                  @input="${this.#onSearchChange}"
                  class="search-input"
                />
              </div>

              <div class="filter-group">
                <label>Filter:</label>
                <select @change="${this.#onFilterChange}">
                  <option value="all">Alle</option>
                  <option value="NotInvited">Ikke inviteret</option>
                  <option value="Invited">Inviteret</option>
                  <option value="Accepted">Accepteret</option>
                </select>
              </div>
            </div>

            <div class="btn-group">
              <uui-button
                look="primary"
                color="positive"
                ?disabled="${this._sending || this._loading}"
                @click="${this.#inviteAll}">
                ${this._sending && !this._sendingMemberId ? "Sender..." : "Send til alle"}
              </uui-button>

              <uui-button
                look="secondary"
                ?disabled="${this._sending || this._loading}"
                @click="${() => this.#loadMembers()}">
                ${this._loading ? "Indlæser..." : "Opdater liste"}
              </uui-button>
            </div>
          </div>

          ${this._result
            ? html`
                <div class="alert ${this._result.success ? "alert-success" : "alert-danger"}">
                  ${this._result.message}
                  ${this._result.sentCount !== undefined
                    ? html`
                        <div class="result-details">
                          Sendt: ${this._result.sentCount} |
                          Sprunget over: ${this._result.skippedCount} |
                          Fejl: ${this._result.errorCount}
                        </div>
                      `
                    : ""}
                </div>
              `
            : ""}

          ${this._loading
            ? html`<div class="loading">Indlæser medlemmer...</div>`
            : html`
                <table class="member-table">
                  <thead>
                    <tr>
                      <th>Navn</th>
                      <th>Email</th>
                      <th>Status</th>
                      <th>Invitation sendt</th>
                      <th>Accepteret</th>
                      <th>Handling</th>
                    </tr>
                  </thead>
                  <tbody>
                    ${filteredMembers.length === 0
                      ? html`
                          <tr>
                            <td colspan="6" class="empty-row">Ingen medlemmer fundet</td>
                          </tr>
                        `
                      : filteredMembers.map(
                          (member) => html`
                            <tr>
                              <td>
                                <a href="/umbraco/section/member-management/workspace/member/edit/${member.memberKey}"
                                   class="member-link"
                                   title="Rediger medlem">
                                  ${member.fullName}
                                </a>
                              </td>
                              <td>${member.email}</td>
                              <td>${this.#getStatusBadge(member.status)}</td>
                              <td>${this.#formatDate(member.invitationSentDate)}</td>
                              <td>${this.#formatDate(member.acceptedDate)}</td>
                              <td>
                                ${member.status === "Accepted"
                                  ? html`<span class="text-muted">-</span>`
                                  : html`
                                      <uui-button
                                        look="primary"
                                        compact
                                        ?disabled="${this._sending}"
                                        @click="${() => this.#inviteMember(member.memberId)}">
                                        ${this._sendingMemberId === member.memberId
                                          ? "Sender..."
                                          : member.status === "Invited"
                                          ? "Send igen"
                                          : "Send invitation"}
                                      </uui-button>
                                    `}
                              </td>
                            </tr>
                          `
                        )}
                  </tbody>
                </table>
              `}
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

    .stats-row {
      display: flex;
      gap: 15px;
      margin-bottom: 20px;
    }

    .stat-box {
      flex: 1;
      padding: 15px;
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 6px;
      text-align: center;
    }

    .stat-box.stat-success {
      background: rgba(76, 175, 80, 0.1);
      border-left: 4px solid #4caf50;
    }

    .stat-box.stat-warning {
      background: rgba(255, 152, 0, 0.1);
      border-left: 4px solid #ff9800;
    }

    .stat-box.stat-default {
      background: rgba(158, 158, 158, 0.1);
      border-left: 4px solid #9e9e9e;
    }

    .stat-number {
      font-size: 2em;
      font-weight: bold;
      color: var(--uui-color-text);
    }

    .stat-label {
      font-size: 0.9em;
      color: var(--uui-color-text-alt);
    }

    .actions-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 20px;
      padding: 15px;
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 6px;
    }

    .filter-controls {
      display: flex;
      align-items: center;
      gap: 20px;
    }

    .search-group {
      display: flex;
      align-items: center;
    }

    .search-input {
      padding: 8px 12px;
      border-radius: 4px;
      border: 1px solid var(--uui-color-border);
      background: var(--uui-color-surface);
      min-width: 250px;
      font-size: 14px;
    }

    .search-input:focus {
      outline: none;
      border-color: var(--uui-color-focus);
      box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.2);
    }

    .search-input::placeholder {
      color: var(--uui-color-text-alt);
    }

    .filter-group {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .filter-group select {
      padding: 8px 12px;
      border-radius: 4px;
      border: 1px solid var(--uui-color-border);
      background: var(--uui-color-surface);
    }

    .btn-group {
      display: flex;
      gap: 10px;
    }

    .member-table {
      width: 100%;
      border-collapse: collapse;
    }

    .member-table th,
    .member-table td {
      padding: 12px;
      text-align: left;
      border-bottom: 1px solid var(--uui-color-border);
    }

    .member-table th {
      background: var(--uui-color-surface-alt);
      font-weight: 600;
    }

    .member-table tbody tr:hover {
      background: var(--uui-color-surface-alt);
    }

    .badge {
      display: inline-block;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 0.85em;
      font-weight: 500;
    }

    .badge-success {
      background: #4caf50;
      color: white;
    }

    .badge-warning {
      background: #ff9800;
      color: white;
    }

    .badge-default {
      background: #9e9e9e;
      color: white;
    }

    .alert {
      padding: 15px;
      border-radius: 6px;
      margin-bottom: 20px;
    }

    .alert-success {
      background: rgba(76, 175, 80, 0.1);
      color: #2e7d32;
      border: 1px solid #4caf50;
    }

    .alert-danger {
      background: rgba(244, 67, 54, 0.1);
      color: #c62828;
      border: 1px solid #f44336;
    }

    .result-details {
      margin-top: 8px;
      font-size: 0.9em;
    }

    .loading {
      text-align: center;
      padding: 40px;
      color: var(--uui-color-text-alt);
    }

    .empty-row {
      text-align: center;
      color: var(--uui-color-text-alt);
      padding: 40px !important;
    }

    .text-muted {
      color: var(--uui-color-text-alt);
    }

    .member-link {
      color: var(--uui-color-interactive);
      text-decoration: none;
      font-weight: 500;
    }

    .member-link:hover {
      color: var(--uui-color-interactive-emphasis);
      text-decoration: underline;
    }
  `;
}

customElements.define("member-invitation-dashboard", MemberInvitationDashboardElement);
