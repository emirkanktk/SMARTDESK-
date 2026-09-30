import { Component } from '@angular/core';

/**
 * Slowly drifting brand colour behind the top of a public page (front page, sign-in). The page
 * needs `position: relative` and `isolation: isolate`, and must clip what reaches past its
 * sides. Faint enough that muted text over it keeps 4.5:1; still with "reduce motion".
 */
@Component({
  selector: 'app-backdrop',
  host: { 'aria-hidden': 'true' },
  template: `
    <span class="blob blob-1"></span>
    <span class="blob blob-2"></span>
    <span class="blob blob-3"></span>
  `,
  styles: `
    :host {
      position: absolute;
      inset: 0 0 auto;
      z-index: -1;
      height: 52rem;
      pointer-events: none;
      mask-image: linear-gradient(#000 60%, transparent);
    }

    .blob {
      position: absolute;
      border-radius: 50%;
      filter: blur(64px);
      animation: drift 18s ease-in-out infinite alternate;
    }
    .blob-1 {
      top: -14rem;
      left: -10rem;
      width: 38rem;
      height: 38rem;
      background: var(--glow-1);
    }
    .blob-2 {
      top: -8rem;
      right: -12rem;
      width: 36rem;
      height: 36rem;
      background: var(--glow-2);
      animation-delay: -6s;
    }
    .blob-3 {
      top: 22rem;
      left: 38%;
      width: 26rem;
      height: 26rem;
      background: var(--glow-3);
      animation-delay: -12s;
    }

    @keyframes drift {
      to {
        transform: translate3d(4rem, 2.5rem, 0) scale(1.08);
      }
    }
  `,
})
export class Backdrop {}
