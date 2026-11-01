/// <reference types="cypress" />

// The end-to-end goal flow:
//   admin logs in → goes to admin → uploads a compiled .idea (added to the library) →
//   creates a page that references it by a {{tag}} → the page renders the widget (no missing placeholder).
//
// Prereqs: the CMS is running (see e2e/README.md) and CYPRESS_ADMIN_PASSWORD is set. The uploaded fixture
// is the Tooltip plugin packed from IdeaCms.Library (copied to cypress/fixtures by the README step).
describe("admin: upload a plugin and reference it from a page", () => {
  const slug = "e2e-plugin-demo";
  const tag = "{{ MindAttic.Ideas.Plugin.Tooltip }}";

  beforeEach(() => cy.loginAsAdmin());

  it("uploads the Tooltip .idea into the library", () => {
    cy.visit("/admin/upload");
    cy.get('input[type="file"]').selectFile(
      "cypress/fixtures/IdeaCms.Plugin.Tooltip.V1.idea",
      { force: true } // the native input is visually hidden inside the dropzone label
    );
    // Install result row reports success (Install or NoOpAlreadyInstalled).
    cy.get(".admin-results li", { timeout: 20000 })
      .should("contain.text", "Tooltip")
      .and(($li) => {
        expect($li.attr("class")).to.contain("ok");
      });
  });

  it("creates a Data page whose body references the widget by tag", () => {
    cy.startNewPage();

    cy.contains("label", "Slug").find("input").clear().type(slug);
    cy.contains("label", "Title").find("input").clear().type("E2E Widget Demo");
    // Kind must be Data for the Body HTML/CSS/JS sections to appear.
    cy.contains("label", "Kind").find("select").select("Data");

    cy.contains("label", "Body HTML")
      .find("textarea")
      .clear()
      .type(`<h1>E2E</h1>${tag}<button data-tooltip="hi">Hover</button>`, {
        parseSpecialCharSequences: false, // keep the literal {{ }} braces
      });

    // Ensure it's published + enabled, then save.
    cy.contains("label", "Published").find('input[type="checkbox"]').check({ force: true });
    cy.contains("label", "Enabled").find('input[type="checkbox"]').check({ force: true });
    cy.contains("button", /save/i).click();
    cy.get(".admin-status.ok", { timeout: 15000 }).should("exist");
  });

  it("renders the page with the widget resolved (no missing-content placeholder)", () => {
    cy.visit(`/${slug}`);
    cy.contains("h1", "E2E").should("be.visible");
    cy.get("button[data-tooltip]").should("exist");
    // The {{tag}} must have resolved — neither the raw token text nor the missing-content box remains.
    cy.contains("IdeaCms.Plugin.Tooltip").should("not.exist");
    cy.contains(/not found/i).should("not.exist");
  });
});
