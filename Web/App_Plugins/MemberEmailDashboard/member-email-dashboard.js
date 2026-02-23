import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

export default class MemberEmailDashboardElement extends UmbElementMixin(LitElement) {
  static properties = {
    _members: { state: true },
    _crews: { state: true },
    _logs: { state: true },
    _loading: { state: true },
    _loadingLogs: { state: true },
    _sending: { state: true },
    _result: { state: true },
    _statusFilter: { state: true },
    _nameSearch: { state: true },
    _emailSearch: { state: true },
    _crewFilter: { state: true },
    _selectedMemberIds: { state: true },
    _emailSubject: { state: true },
    _emailBody: { state: true },
    _expandedLogId: { state: true },
    _activeTab: { state: true },
  };

  #authContext;

  constructor() {
    super();
    this._members = [];
    this._crews = [];
    this._logs = [];
    this._loading = false;
    this._loadingLogs = false;
    this._sending = false;
    this._result = null;
    this._statusFilter = "Invited";
    this._nameSearch = "";
    this._emailSearch = "";
    this._crewFilter = "";
    this._selectedMemberIds = new Set();
    this._emailSubject = "";
    this._emailBody = "";
    this._expandedLogId = null;
    this._activeTab = "send"; // "send" | "history"

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });
  }

  connectedCallback() {
    super.connectedCallback();
    this.#loadData();
  }

  async #getAuthHeaders() {
    const config = this.#authContext.getOpenApiConfiguration();
    const authToken = await config.token();
    return { "Authorization": `Bearer ${authToken}`, credentials: config.credentials };
  }

  async #loadData() {
    if (!this.#authContext) {
      setTimeout(() => this.#loadData(), 100);
      return;
    }

    this._loading = true;
    this._result = null;

    try {
      const { Authorization, credentials } = await this.#getAuthHeaders();
      const headers = { Authorization };

      const [membersRes, crewsRes] = await Promise.all([
        fetch("/umbraco/management/api/v1/memberemaildashboard/members", { method: "GET", credentials, headers }),
        fetch("/umbraco/management/api/v1/memberemaildashboard/crews", { method: "GET", credentials, headers }),
      ]);

      if (!membersRes.ok) throw new Error(`Server error loading members: ${membersRes.status}`);
      if (!crewsRes.ok) throw new Error(`Server error loading crews: ${crewsRes.status}`);

      const membersData = await membersRes.json();
      const crewsData = await crewsRes.json();

      this._members = membersData.members || [];
      this._crews = crewsData.crews || [];
      this._selectedMemberIds = new Set();
    } catch (error) {
      this._result = { success: false, message: error.message || "Failed to load data" };
    } finally {
      this._loading = false;
    }
  }

  async #loadLogs() {
    if (!this.#authContext) return;
    this._loadingLogs = true;
    try {
      const { Authorization, credentials } = await this.#getAuthHeaders();
      const res = await fetch("/umbraco/management/api/v1/memberemaildashboard/logs", {
        method: "GET",
        credentials,
        headers: { Authorization },
      });
      if (!res.ok) throw new Error(`Server error loading logs: ${res.status}`);
      const data = await res.json();
      this._logs = data.logs || [];
    } catch (error) {
      this._logs = [];
    } finally {
      this._loadingLogs = false;
    }
  }

  async #sendEmails() {
    if (!this.#authContext) return;
    if (!this._emailSubject.trim() || !this._emailBody.trim()) {
      this._result = { success: false, message: "Udfyld emne og besked før afsendelse" };
      return;
    }
    if (this._selectedMemberIds.size === 0) {
      this._result = { success: false, message: "Vælg mindst ét medlem" };
      return;
    }

    const count = this._selectedMemberIds.size;
    if (!confirm(`Er du sikker på, at du vil sende email til ${count} ${count === 1 ? "modtager" : "modtagere"}?`)) return;

    this._sending = true;
    this._result = null;

    try {
      const { Authorization, credentials } = await this.#getAuthHeaders();
      const response = await fetch("/umbraco/management/api/v1/memberemaildashboard/send", {
        method: "POST",
        credentials,
        headers: { Authorization, "Content-Type": "application/json" },
        body: JSON.stringify({
          memberIds: Array.from(this._selectedMemberIds),
          subject: this._emailSubject,
          body: this._emailBody,
        }),
      });

      const data = await response.json();
      this._result = data;

      if (data.success) {
        this._selectedMemberIds = new Set();
        this._emailSubject = "";
        this._emailBody = "";
      }
    } catch (error) {
      this._result = { success: false, message: error.message || "Failed to send emails" };
    } finally {
      this._sending = false;
    }
  }

  #getFilteredMembers() {
    let filtered = this._members;

    if (this._statusFilter !== "all") {
      filtered = filtered.filter((m) => m.status === this._statusFilter);
    }
    if (this._nameSearch.trim()) {
      const q = this._nameSearch.toLowerCase().trim();
      filtered = filtered.filter((m) => m.fullName && m.fullName.toLowerCase().includes(q));
    }
    if (this._emailSearch.trim()) {
      const q = this._emailSearch.toLowerCase().trim();
      filtered = filtered.filter((m) => m.email && m.email.toLowerCase().includes(q));
    }
    if (this._crewFilter) {
      const crewId = parseInt(this._crewFilter, 10);
      filtered = filtered.filter((m) => m.crewIds && m.crewIds.includes(crewId));
    }
    return filtered;
  }

  #selectAll() {
    const newSet = new Set(this._selectedMemberIds);
    this.#getFilteredMembers().forEach((m) => newSet.add(m.memberId));
    this._selectedMemberIds = newSet;
  }

  #deselectAll() {
    const newSet = new Set(this._selectedMemberIds);
    this.#getFilteredMembers().forEach((m) => newSet.delete(m.memberId));
    this._selectedMemberIds = newSet;
  }

  #toggleMember(memberId) {
    const newSet = new Set(this._selectedMemberIds);
    newSet.has(memberId) ? newSet.delete(memberId) : newSet.add(memberId);
    this._selectedMemberIds = newSet;
  }

  #switchTab(tab) {
    this._activeTab = tab;
    if (tab === "history" && this._logs.length === 0) {
      this.#loadLogs();
    }
  }

  #toggleLogExpand(logId) {
    this._expandedLogId = this._expandedLogId === logId ? null : logId;
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
    return new Date(dateString).toLocaleDateString("da-DK", {
      day: "2-digit", month: "2-digit", year: "numeric",
    });
  }

  #formatDateTime(dateString) {
    if (!dateString) return "-";
    return new Date(dateString).toLocaleDateString("da-DK", {
      day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
    });
  }

  #getCounts() {
    const total = this._members.length;
    const accepted = this._members.filter((m) => m.status === "Accepted").length;
    const invited = this._members.filter((m) => m.status === "Invited").length;
    const notInvited = this._members.filter((m) => m.status === "NotInvited").length;
    return { total, accepted, invited, notInvited };
  }

  // ── Filtered members have a mix of hasInvitationUrl values
  #filteredHaveInvitationUrls() {
    return this.#getFilteredMembers().some((m) => m.hasInvitationUrl);
  }

  render() {
    return html`
      <uui-box headline="Send Email til Frivillige">
        <div class="content">

          <!-- Tabs -->
          <div class="tabs">
            <button
              class="tab ${this._activeTab === "send" ? "tab-active" : ""}"
              @click="${() => this.#switchTab("send")}">
              Send Email
            </button>
            <button
              class="tab ${this._activeTab === "history" ? "tab-active" : ""}"
              @click="${() => this.#switchTab("history")}">
              Email Historik
            </button>
          </div>

          ${this._activeTab === "send" ? this.#renderSendTab() : this.#renderHistoryTab()}

        </div>
      </uui-box>
    `;
  }

  #renderSendTab() {
    const counts = this.#getCounts();
    const filteredMembers = this.#getFilteredMembers();

    return html`
      <!-- Stats -->
      <div class="stats-row">
        <div class="stat-box">
          <div class="stat-number">${counts.total}</div>
          <div class="stat-label">Total</div>
        </div>
        <div class="stat-box stat-warning">
          <div class="stat-number">${counts.invited}</div>
          <div class="stat-label">Inviteret (ikke accepteret)</div>
        </div>
        <div class="stat-box stat-success">
          <div class="stat-number">${counts.accepted}</div>
          <div class="stat-label">Accepteret</div>
        </div>
        <div class="stat-box stat-default">
          <div class="stat-number">${counts.notInvited}</div>
          <div class="stat-label">Ikke inviteret</div>
        </div>
        <div class="stat-box stat-selected">
          <div class="stat-number">${this._selectedMemberIds.size}</div>
          <div class="stat-label">Valgte modtagere</div>
        </div>
      </div>

      <!-- Filters -->
      <div class="filter-panel">
        <div class="filter-row">
          <div class="filter-group">
            <label>Status</label>
            <select .value="${this._statusFilter}" @change="${(e) => { this._statusFilter = e.target.value; }}">
              <option value="all">Alle</option>
              <option value="Invited" ?selected="${this._statusFilter === "Invited"}">Inviteret (ikke accepteret)</option>
              <option value="Accepted" ?selected="${this._statusFilter === "Accepted"}">Accepteret</option>
              <option value="NotInvited" ?selected="${this._statusFilter === "NotInvited"}">Ikke inviteret</option>
            </select>
          </div>

          <div class="filter-group">
            <label>Navn</label>
            <input type="text" placeholder="Søg navn..." .value="${this._nameSearch}"
              @input="${(e) => { this._nameSearch = e.target.value; }}" class="filter-input" />
          </div>

          <div class="filter-group">
            <label>Email</label>
            <input type="text" placeholder="Søg email..." .value="${this._emailSearch}"
              @input="${(e) => { this._emailSearch = e.target.value; }}" class="filter-input" />
          </div>

          <div class="filter-group">
            <label>Crew</label>
            <select .value="${this._crewFilter}" @change="${(e) => { this._crewFilter = e.target.value; }}">
              <option value="">Alle crews</option>
              ${this._crews.map((crew) => html`<option value="${crew.id}">${crew.name}</option>`)}
            </select>
          </div>

          <div class="filter-group filter-actions">
            <uui-button look="secondary" ?disabled="${this._loading}" @click="${() => this.#loadData()}">
              ${this._loading ? "Indlæser..." : "Opdater"}
            </uui-button>
          </div>
        </div>

        <div class="selection-row">
          <span class="filtered-count">${filteredMembers.length} medlemmer vist</span>
          <div class="selection-btns">
            <uui-button look="secondary" compact @click="${this.#selectAll}">Vælg alle synlige</uui-button>
            <uui-button look="secondary" compact @click="${this.#deselectAll}">Fravælg alle synlige</uui-button>
          </div>
        </div>
      </div>

      <!-- Result -->
      ${this._result ? html`
        <div class="alert ${this._result.success ? "alert-success" : "alert-danger"}">
          <strong>${this._result.message}</strong>
          ${this._result.sentCount !== undefined ? html`
            <div class="result-details">Sendt: ${this._result.sentCount} &nbsp;|&nbsp; Fejl: ${this._result.errorCount}</div>
          ` : ""}
          ${this._result.errors && this._result.errors.length > 0 ? html`
            <ul class="error-list">${this._result.errors.map((e) => html`<li>${e}</li>`)}</ul>
          ` : ""}
        </div>
      ` : ""}

      <!-- Member table -->
      ${this._loading
        ? html`<div class="loading">Indlæser medlemmer...</div>`
        : html`
          <div class="table-wrap">
            <table class="member-table">
              <thead>
                <tr>
                  <th class="col-check"></th>
                  <th>Navn</th>
                  <th>Email</th>
                  <th>Status</th>
                  <th>Crew(s)</th>
                  <th>Inv. link</th>
                  <th>Invitation sendt</th>
                  <th>Accepteret</th>
                </tr>
              </thead>
              <tbody>
                ${filteredMembers.length === 0
                  ? html`<tr><td colspan="8" class="empty-row">Ingen medlemmer fundet</td></tr>`
                  : filteredMembers.map((member) => html`
                    <tr
                      class="${this._selectedMemberIds.has(member.memberId) ? "row-selected" : ""}"
                      @click="${() => this.#toggleMember(member.memberId)}">
                      <td class="col-check" @click="${(e) => e.stopPropagation()}">
                        <input type="checkbox"
                          .checked="${this._selectedMemberIds.has(member.memberId)}"
                          @change="${() => this.#toggleMember(member.memberId)}" />
                      </td>
                      <td>
                        <a href="/umbraco/section/member-management/workspace/member/edit/${member.memberKey}"
                          class="member-link" title="Rediger medlem"
                          @click="${(e) => e.stopPropagation()}">${member.fullName}</a>
                      </td>
                      <td>${member.email}</td>
                      <td>${this.#getStatusBadge(member.status)}</td>
                      <td class="crew-cell">
                        ${member.crewNames && member.crewNames.length > 0
                          ? member.crewNames.join(", ")
                          : html`<span class="text-muted">-</span>`}
                      </td>
                      <td class="center-cell">
                        ${member.hasInvitationUrl
                          ? html`<span class="badge badge-link" title="Har aktivt invitationslink">✓</span>`
                          : html`<span class="text-muted">-</span>`}
                      </td>
                      <td>${this.#formatDate(member.invitationSentDate)}</td>
                      <td>${this.#formatDate(member.acceptedDate)}</td>
                    </tr>
                  `)}
              </tbody>
            </table>
          </div>
        `}

      <!-- Compose -->
      ${this._selectedMemberIds.size > 0 ? html`
        <div class="compose-section">
          <div class="compose-header">
            <h3>Skriv email til ${this._selectedMemberIds.size} ${this._selectedMemberIds.size === 1 ? "modtager" : "modtagere"}</h3>
            <div class="placeholder-hints">
              <strong>Variabler:</strong>
              <code>{{firstName}}</code>
              <code>{{lastName}}</code>
              <code>{{email}}</code>
              <code>{{phone}}</code>
              <code>{{selectedCrews}}</code>
              <code>{{portalUrl}}</code>
              ${this.#filteredHaveInvitationUrls() ? html`
                <code title="Indsætter en tilmeldingsknap — kun for medlemmer med aktivt invitationslink">{{invitationUrl}}</code>
                <span class="inv-hint">(knap til invitationslink — tomt hvis ingen aktiv token)</span>
              ` : ""}
            </div>
          </div>

          <div class="compose-field">
            <label for="email-subject">Emne</label>
            <input id="email-subject" type="text" placeholder="Email emne..."
              .value="${this._emailSubject}"
              @input="${(e) => { this._emailSubject = e.target.value; }}"
              class="compose-input" />
          </div>

          <div class="compose-field">
            <label for="email-body">Besked (HTML understøttes)</label>
            <textarea id="email-body"
              placeholder="Skriv din besked her...

HTML og variabler som {{firstName}} kan bruges.
Brug {{invitationUrl}} for at indsætte en tilmeldingsknap til de modtagere der har et aktivt link."
              .value="${this._emailBody}"
              @input="${(e) => { this._emailBody = e.target.value; }}"
              class="compose-textarea" rows="10"></textarea>
          </div>

          <div class="compose-actions">
            <uui-button look="primary" color="positive"
              ?disabled="${this._sending || !this._emailSubject.trim() || !this._emailBody.trim()}"
              @click="${this.#sendEmails}">
              ${this._sending
                ? "Sender..."
                : `Send til ${this._selectedMemberIds.size} ${this._selectedMemberIds.size === 1 ? "modtager" : "modtagere"}`}
            </uui-button>
          </div>
        </div>
      ` : html`
        <div class="compose-hint">
          Vælg et eller flere medlemmer i tabellen ovenfor for at sende email.
        </div>
      `}
    `;
  }

  #renderHistoryTab() {
    return html`
      <div class="history-section">
        <div class="history-toolbar">
          <h3 class="history-title">Sendte emails</h3>
          <uui-button look="secondary" ?disabled="${this._loadingLogs}" @click="${() => this.#loadLogs()}">
            ${this._loadingLogs ? "Indlæser..." : "Opdater"}
          </uui-button>
        </div>

        ${this._loadingLogs
          ? html`<div class="loading">Indlæser historik...</div>`
          : this._logs.length === 0
          ? html`<div class="empty-history">Ingen emails sendt endnu.</div>`
          : this._logs.map((log) => html`
            <div class="log-entry">
              <div class="log-header" @click="${() => this.#toggleLogExpand(log.id)}">
                <div class="log-meta">
                  <span class="log-date">${this.#formatDateTime(log.sentAt)}</span>
                  <span class="log-subject">${log.subject}</span>
                </div>
                <div class="log-stats">
                  <span class="badge badge-success">${log.sentCount} sendt</span>
                  ${log.errorCount > 0
                    ? html`<span class="badge badge-danger">${log.errorCount} fejl</span>`
                    : ""}
                  <span class="log-toggle">${this._expandedLogId === log.id ? "▲" : "▼"}</span>
                </div>
              </div>

              ${this._expandedLogId === log.id ? html`
                <div class="log-detail">
                  <div class="log-body-preview">
                    <strong>Besked (preview):</strong>
                    <div class="log-body-text">${log.body}</div>
                  </div>

                  <div class="log-recipients">
                    <strong>Modtagere (${log.recipients.length}):</strong>
                    <table class="recipients-table">
                      <thead>
                        <tr>
                          <th>Navn</th>
                          <th>Email</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        ${log.recipients.map((r) => html`
                          <tr>
                            <td>${r.fullName || "-"}</td>
                            <td>${r.email}</td>
                            <td>
                              ${r.success
                                ? html`<span class="badge badge-success">Sendt</span>`
                                : html`<span class="badge badge-danger" title="${r.errorMessage}">Fejl</span>`}
                            </td>
                          </tr>
                        `)}
                      </tbody>
                    </table>
                  </div>
                </div>
              ` : ""}
            </div>
          `)}
      </div>
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

    /* Tabs */
    .tabs {
      display: flex;
      gap: 0;
      margin-bottom: 24px;
      border-bottom: 2px solid var(--uui-color-border);
    }

    .tab {
      padding: 10px 20px;
      border: none;
      background: none;
      cursor: pointer;
      font-size: 14px;
      font-weight: 500;
      color: var(--uui-color-text-alt);
      border-bottom: 2px solid transparent;
      margin-bottom: -2px;
    }

    .tab:hover {
      color: var(--uui-color-text);
    }

    .tab.tab-active {
      color: var(--uui-color-interactive);
      border-bottom-color: var(--uui-color-interactive);
    }

    /* Stats */
    .stats-row {
      display: flex;
      gap: 12px;
      margin-bottom: 20px;
      flex-wrap: wrap;
    }

    .stat-box {
      flex: 1;
      min-width: 100px;
      padding: 14px;
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 6px;
      text-align: center;
    }

    .stat-box.stat-success { background: rgba(76, 175, 80, 0.1); border-left: 4px solid #4caf50; }
    .stat-box.stat-warning { background: rgba(255, 152, 0, 0.1); border-left: 4px solid #ff9800; }
    .stat-box.stat-default { background: rgba(158, 158, 158, 0.1); border-left: 4px solid #9e9e9e; }
    .stat-box.stat-selected { background: rgba(59, 130, 246, 0.1); border-left: 4px solid #3b82f6; }

    .stat-number { font-size: 2em; font-weight: bold; color: var(--uui-color-text); }
    .stat-label { font-size: 0.85em; color: var(--uui-color-text-alt); margin-top: 4px; }

    /* Filters */
    .filter-panel {
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 6px;
      padding: 16px;
      margin-bottom: 20px;
    }

    .filter-row {
      display: flex;
      gap: 16px;
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .filter-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .filter-group label {
      font-size: 0.82em;
      font-weight: 600;
      color: var(--uui-color-text-alt);
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .filter-group select,
    .filter-input {
      padding: 7px 10px;
      border-radius: 4px;
      border: 1px solid var(--uui-color-border);
      background: var(--uui-color-surface);
      font-size: 14px;
      min-width: 155px;
    }

    .filter-group select:focus,
    .filter-input:focus {
      outline: none;
      border-color: var(--uui-color-focus);
    }

    .filter-actions { justify-content: flex-end; }

    .selection-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-top: 12px;
      padding-top: 12px;
      border-top: 1px solid var(--uui-color-border);
    }

    .filtered-count { font-size: 0.9em; color: var(--uui-color-text-alt); }
    .selection-btns { display: flex; gap: 8px; }

    /* Table */
    .table-wrap {
      overflow-x: auto;
      margin-bottom: 20px;
    }

    .member-table {
      width: 100%;
      border-collapse: collapse;
    }

    .member-table th,
    .member-table td {
      padding: 10px 12px;
      text-align: left;
      border-bottom: 1px solid var(--uui-color-border);
      white-space: nowrap;
    }

    .member-table th {
      background: var(--uui-color-surface-alt);
      font-weight: 600;
      font-size: 0.9em;
    }

    .member-table tbody tr { cursor: pointer; }
    .member-table tbody tr:hover { background: var(--uui-color-surface-alt); }
    .member-table tbody tr.row-selected { background: rgba(59, 130, 246, 0.08); }

    .col-check { width: 36px; }
    .center-cell { text-align: center; }

    .crew-cell {
      max-width: 200px;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* Badges */
    .badge {
      display: inline-block;
      padding: 3px 8px;
      border-radius: 4px;
      font-size: 0.82em;
      font-weight: 500;
    }

    .badge-success { background: #4caf50; color: white; }
    .badge-warning { background: #ff9800; color: white; }
    .badge-default { background: #9e9e9e; color: white; }
    .badge-danger  { background: #f44336; color: white; }
    .badge-link    { background: #3b82f6; color: white; }

    /* Alerts */
    .alert {
      padding: 14px 16px;
      border-radius: 6px;
      margin-bottom: 20px;
    }

    .alert-success { background: rgba(76, 175, 80, 0.1); color: #2e7d32; border: 1px solid #4caf50; }
    .alert-danger  { background: rgba(244, 67, 54, 0.1); color: #c62828; border: 1px solid #f44336; }

    .result-details { margin-top: 6px; font-size: 0.9em; }
    .error-list { margin: 8px 0 0; padding-left: 20px; font-size: 0.85em; }

    /* Compose */
    .compose-section {
      background: var(--uui-color-surface-alt, #f9f9f9);
      border: 1px solid var(--uui-color-border);
      border-radius: 6px;
      padding: 20px;
      margin-top: 8px;
    }

    .compose-header { margin-bottom: 16px; }
    .compose-header h3 { margin: 0 0 8px; font-size: 1.1em; }

    .placeholder-hints {
      font-size: 0.85em;
      color: var(--uui-color-text-alt);
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
      align-items: center;
    }

    .placeholder-hints code {
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: 3px;
      padding: 1px 5px;
      font-size: 0.9em;
    }

    .inv-hint {
      font-style: italic;
      font-size: 0.9em;
      color: var(--uui-color-text-alt);
    }

    .compose-field { margin-bottom: 14px; display: flex; flex-direction: column; gap: 5px; }
    .compose-field label { font-weight: 600; font-size: 0.9em; }

    .compose-input {
      padding: 8px 12px;
      border-radius: 4px;
      border: 1px solid var(--uui-color-border);
      background: var(--uui-color-surface);
      font-size: 14px;
    }

    .compose-input:focus { outline: none; border-color: var(--uui-color-focus); }

    .compose-textarea {
      padding: 10px 12px;
      border-radius: 4px;
      border: 1px solid var(--uui-color-border);
      background: var(--uui-color-surface);
      font-size: 13px;
      font-family: monospace;
      resize: vertical;
    }

    .compose-textarea:focus { outline: none; border-color: var(--uui-color-focus); }
    .compose-actions { margin-top: 4px; }

    .compose-hint {
      text-align: center;
      padding: 24px;
      color: var(--uui-color-text-alt);
      font-style: italic;
      border: 2px dashed var(--uui-color-border);
      border-radius: 6px;
      margin-top: 8px;
    }

    /* History */
    .history-section { margin-top: 4px; }

    .history-toolbar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }

    .history-title { margin: 0; font-size: 1.1em; }

    .empty-history {
      text-align: center;
      padding: 40px;
      color: var(--uui-color-text-alt);
      font-style: italic;
    }

    .log-entry {
      border: 1px solid var(--uui-color-border);
      border-radius: 6px;
      margin-bottom: 10px;
      overflow: hidden;
    }

    .log-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 16px;
      background: var(--uui-color-surface-alt);
      cursor: pointer;
      gap: 16px;
    }

    .log-header:hover { background: var(--uui-color-surface-emphasis, #e8e8e8); }

    .log-meta {
      display: flex;
      flex-direction: column;
      gap: 2px;
      overflow: hidden;
    }

    .log-date {
      font-size: 0.82em;
      color: var(--uui-color-text-alt);
    }

    .log-subject {
      font-weight: 600;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .log-stats {
      display: flex;
      gap: 8px;
      align-items: center;
      flex-shrink: 0;
    }

    .log-toggle {
      font-size: 0.8em;
      color: var(--uui-color-text-alt);
    }

    .log-detail {
      padding: 16px;
      border-top: 1px solid var(--uui-color-border);
      background: var(--uui-color-surface);
    }

    .log-body-preview {
      margin-bottom: 16px;
      font-size: 0.9em;
    }

    .log-body-text {
      margin-top: 6px;
      padding: 10px;
      background: var(--uui-color-surface-alt);
      border-radius: 4px;
      font-family: monospace;
      font-size: 0.9em;
      max-height: 120px;
      overflow-y: auto;
      white-space: pre-wrap;
      word-break: break-word;
    }

    .log-recipients { font-size: 0.9em; }

    .recipients-table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 8px;
    }

    .recipients-table th,
    .recipients-table td {
      padding: 8px 10px;
      text-align: left;
      border-bottom: 1px solid var(--uui-color-border);
      font-size: 0.9em;
    }

    .recipients-table th {
      background: var(--uui-color-surface-alt);
      font-weight: 600;
    }

    /* Misc */
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

    .text-muted { color: var(--uui-color-text-alt); }

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

customElements.define("member-email-dashboard", MemberEmailDashboardElement);
