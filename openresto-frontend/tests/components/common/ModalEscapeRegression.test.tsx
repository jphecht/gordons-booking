// Regression test for issue #140 Correction #6.
//
// The investigation found that every modal in this codebase is built on React
// Native's <Modal onRequestClose={...}>, and react-native-web's Modal
// implementation already attaches a document-level `keyup` listener that calls
// onRequestClose() when e.key === 'Escape' (see
// node_modules/react-native-web/dist/exports/Modal/ModalContent.js). That part
// of the corrected spec is accurate.
//
// What the spec got wrong: it assumed a Jest test in jsdom could exercise that
// DOM-level behavior directly. It can't. This repo's Jest config
// (openresto-frontend/package.json -> "jest") uses "jest-expo"'s default
// (native) preset, which resolves `react-native` to the real react-native
// package and renders through `react-test-renderer` — not react-dom. Only
// `jest-expo`'s dedicated web preset (unused here) aliases `react-native` to
// `react-native-web` and renders via jsdom+react-dom. Forcing that alias in a
// single test (`jest.mock("react-native", () => require("react-native-web"))`)
// was tried and fails: react-test-renderer's host config cannot mount
// react-native-web's DOM-based output, so a simulated `keyup` never reaches
// react-native-web's listener. Escape-to-close is real, but only observable in
// an actual browser — i.e. Playwright E2E, per the spec's own Test approach
// section, not a Jest unit test.
//
// So this test verifies the one thing that IS this codebase's responsibility
// and IS observable via react-test-renderer: every existing modal wires
// `onRequestClose` to its close handler, which is the sole precondition for
// react-native-web's built-in Escape handling to fire in the browser. No new
// production code was added for this.
import React from "react";
import TestRenderer from "react-test-renderer";
import { Modal } from "react-native";
import ConfirmModal from "@/components/common/ConfirmModal";
import AlertModal from "@/components/common/AlertModal";
import { BrandProvider } from "@/context/BrandContext";

global.fetch = jest.fn(() =>
  Promise.resolve({
    ok: true,
    json: () => Promise.resolve({ appName: "Open Resto", primaryColor: "#0a7ea4" }),
  })
) as jest.Mock;

jest.mock("@/hooks/use-color-scheme", () => ({
  useColorScheme: () => "light",
}));

describe("Existing modals wire onRequestClose (precondition for web Escape-to-close)", () => {
  const mountedTrees: TestRenderer.ReactTestRenderer[] = [];

  afterEach(async () => {
    await TestRenderer.act(async () => {
      for (const tree of mountedTrees.splice(0)) tree.unmount();
    });
  });

  it("wires ConfirmModal's onRequestClose to its onCancel handler", async () => {
    const onCancel = jest.fn();
    let tree!: TestRenderer.ReactTestRenderer;
    await TestRenderer.act(async () => {
      tree = TestRenderer.create(
        <BrandProvider>
          <ConfirmModal
            visible
            message="Cancel booking?"
            onConfirm={jest.fn()}
            onCancel={onCancel}
          />
        </BrandProvider>
      );
      mountedTrees.push(tree);
    });

    const modal = tree.root.findByType(Modal);
    expect(modal.props.onRequestClose).toBe(onCancel);

    modal.props.onRequestClose();
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("wires AlertModal's onRequestClose to its onClose handler", async () => {
    const onClose = jest.fn();
    let tree!: TestRenderer.ReactTestRenderer;
    await TestRenderer.act(async () => {
      tree = TestRenderer.create(
        <BrandProvider>
          <AlertModal visible message="Something happened" onClose={onClose} />
        </BrandProvider>
      );
      mountedTrees.push(tree);
    });

    const modal = tree.root.findByType(Modal);
    expect(modal.props.onRequestClose).toBe(onClose);

    modal.props.onRequestClose();
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
