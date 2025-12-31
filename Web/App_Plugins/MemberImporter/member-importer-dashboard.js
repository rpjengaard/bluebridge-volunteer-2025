import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { LitElement, html, css } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";

export default class MemberImporterDashboardElement extends UmbElementMixin(LitElement) {
  static properties = {
    _selectedFile: { state: true },
    _importing: { state: true },
    _importResult: { state: true },
  };

  #authContext;

  constructor() {
    super();
    this._selectedFile = null;
    this._importing = false;
    this._importResult = null;

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      this.#authContext = authContext;
    });
  }

  #onFileSelected(event) {
    const input = event.target;
    if (input.files && input.files[0]) {
      this._selectedFile = input.files[0];
    }
  }

  async #onImportCsv() {
    if (!this._selectedFile) {
      alert("Please select a CSV file first");
      return;
    }

    if (!this.#authContext) {
      this._importResult = {
        success: false,
        message: "Authentication context not available. Please refresh the page.",
      };
      return;
    }

    this._importing = true;
    this._importResult = null;

    const formData = new FormData();
    formData.append("file", this._selectedFile);

    try {
      // Get the auth configuration - token is a function that returns the bearer token
      const config = this.#authContext.getOpenApiConfiguration();
      const authToken = await config.token();

      const response = await fetch("/umbraco/management/api/v1/memberimporter/importcsv", {
        method: "POST",
        body: formData,
        credentials: config.credentials,
        headers: {
          "Authorization": `Bearer ${authToken}`,
        },
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Server error: ${response.status} - ${errorText || response.statusText}`);
      }

      const data = await response.json();
      this._importing = false;
      this._importResult = data;
    } catch (error) {
      this._importing = false;
      const errorMessage = error instanceof Error ? error.message : "An error occurred during import";
      this._importResult = {
        success: false,
        message: errorMessage,
      };
    }
  }

  #clearResults() {
    this._importResult = null;
    this._selectedFile = null;
    const fileInput = this.shadowRoot?.querySelector('input[type="file"]');
    if (fileInput) {
      fileInput.value = "";
    }
  }

  render() {
    return html`
      <uui-box headline="Member CSV Importer">
        <div class="content">
          <h3>Import Members from CSV</h3>
          <p>Upload a CSV file to import members into the system. The CSV should contain the following columns:</p>
          <ul>
            <li><strong>Email</strong> (required) - Will be used as username</li>
            <li><strong>Fornavn</strong> - First name</li>
            <li><strong>Efternavn</strong> - Last name</li>
            <li><strong>Telefon</strong> - Phone number</li>
            <li><strong>Arbejdssteder</strong> - Previous workplaces</li>
          </ul>

          <div class="import-form">
            <uui-label>Select CSV File</uui-label>
            <input type="file" accept=".csv" @change="${this.#onFileSelected}" />

            <div class="btn-group">
              <uui-button
                look="primary"
                color="positive"
                ?disabled="${!this._selectedFile || this._importing}"
                @click="${this.#onImportCsv}">
                ${this._importing ? "Importing..." : "Import Members"}
              </uui-button>

              <uui-button
                look="secondary"
                ?disabled="${this._importing}"
                @click="${this.#clearResults}">
                Clear Results
              </uui-button>
            </div>
          </div>

          ${this._importing
            ? html`
                <div class="alert alert-info">
                  Processing CSV file... Please wait.
                </div>
              `
            : ""}

          ${this._importResult?.success
            ? html`
                <div class="alert alert-success">
                  <strong>Success!</strong> ${this._importResult.message}
                  <div class="summary">
                    <strong>Summary:</strong>
                    <ul>
                      <li>Total rows processed: ${this._importResult.results?.totalRows ?? 0}</li>
                      <li>Successfully imported: ${this._importResult.results?.successCount ?? 0}</li>
                      <li>Skipped: ${this._importResult.results?.skippedCount ?? 0}</li>
                      <li>Errors: ${this._importResult.results?.errorCount ?? 0}</li>
                    </ul>
                  </div>
                </div>
              `
            : ""}

          ${this._importResult && !this._importResult.success
            ? html`
                <div class="alert alert-danger">
                  <strong>Error!</strong> ${this._importResult.message}
                </div>
              `
            : ""}

          ${this._importResult?.results?.errors?.length > 0
            ? html`
                <div class="alert alert-warning errors-list">
                  <strong>Import Errors:</strong>
                  <ul>
                    ${this._importResult.results.errors.map((error) => html`<li>${error}</li>`)}
                  </ul>
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

    .content ul {
      margin-left: 20px;
      margin-bottom: 20px;
    }

    .content li {
      margin-bottom: 5px;
    }

    .import-form {
      margin-top: 20px;
      padding: 20px;
      background: var(--uui-color-surface-alt, #f5f5f5);
      border-radius: 6px;
    }

    .import-form uui-label {
      display: block;
      margin-bottom: 8px;
      font-weight: 600;
    }

    .import-form input[type="file"] {
      display: block;
      padding: 10px;
      margin-bottom: 20px;
      width: 100%;
      box-sizing: border-box;
    }

    .btn-group {
      display: flex;
      gap: 10px;
    }

    .alert {
      margin-top: 20px;
      padding: 15px;
      border-radius: 6px;
    }

    .alert-info {
      background: var(--uui-color-current, #1b264f);
      color: white;
    }

    .alert-success {
      background: var(--uui-color-positive, #4caf50);
      color: white;
    }

    .alert-danger {
      background: var(--uui-color-danger, #f44336);
      color: white;
    }

    .alert-warning {
      background: var(--uui-color-warning, #ff9800);
      color: black;
    }

    .summary {
      margin-top: 10px;
    }

    .summary ul {
      margin-left: 20px;
      margin-top: 5px;
    }

    .errors-list {
      max-height: 300px;
      overflow-y: auto;
    }

    .errors-list ul {
      margin-left: 20px;
      margin-top: 10px;
    }

    h3 {
      margin-top: 0;
      margin-bottom: 15px;
    }
  `;
}

customElements.define("member-importer-dashboard", MemberImporterDashboardElement);
