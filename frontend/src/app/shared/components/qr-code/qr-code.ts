import { Component, computed, input } from '@angular/core';
import { qrModules } from '../../utils/qr';

/** Modules of white around the code, as scanners expect. */
const QUIET_ZONE = 4;

/**
 * A QR code for `value`, drawn as an SVG that fills the width the parent gives it. Always
 * black on white, whatever the theme: that's what phone cameras read best.
 */
@Component({
  selector: 'app-qr-code',
  template: `
    <svg
      [attr.viewBox]="viewBox()"
      role="img"
      [attr.aria-label]="label()"
      shape-rendering="crispEdges"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect
        [attr.x]="-quietZone"
        [attr.y]="-quietZone"
        [attr.width]="side()"
        [attr.height]="side()"
        fill="#fff"
      />
      <path [attr.d]="modules().path" fill="#000" />
    </svg>
  `,
  styles: `
    :host {
      display: block;
    }
    svg {
      display: block;
      width: 100%;
      height: auto;
    }
  `,
})
export class QrCode {
  readonly value = input.required<string>();
  /** What the code is for, read out in its place: "QR code for checking in to Board Room". */
  readonly label = input.required<string>();

  protected readonly quietZone = QUIET_ZONE;
  protected readonly modules = computed(() => qrModules(this.value()));
  protected readonly side = computed(() => this.modules().size + 2 * QUIET_ZONE);
  protected readonly viewBox = computed(
    () => `${-QUIET_ZONE} ${-QUIET_ZONE} ${this.side()} ${this.side()}`,
  );
}
