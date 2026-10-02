import { DOCUMENT } from '@angular/common';
import { Component, OnInit, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AssistantService } from '../../core/services/assistant.service';
import { AuthService } from '../../core/services/auth.service';
import { Icon } from '../../shared/components/icon/icon';
import { GUIDE_SECTIONS } from './guide-sections';

/**
 * How SmartDesk works (`/guide`): each part of the app in a few steps, with a link to where it's
 * done. A manager also sees their team's part, an admin the office's; the assistant's only when
 * the server has one. The parts have addresses (`/guide#door`) to share or come back to.
 */
@Component({
  imports: [RouterLink, Icon],
  selector: 'app-guide',
  styleUrl: './guide.scss',
  templateUrl: './guide.html',
})
export class Guide implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly assistant = inject(AssistantService);
  private readonly route = inject(ActivatedRoute);
  private readonly document = inject(DOCUMENT);

  private readonly assistantEnabled = signal(false);

  /** The parts for this user's roles. */
  protected readonly sections = computed(() => {
    // Read the user so this follows a sign-in or a change of role.
    this.auth.currentUser();
    const assistant = this.assistantEnabled();
    return GUIDE_SECTIONS.filter(
      (section) =>
        (!section.roles || section.roles.some((role) => this.auth.hasRole(role))) &&
        (!section.needsAssistant || assistant),
    );
  });

  constructor() {
    // Opened at a part's address: show that part.
    afterNextRender(() => {
      const fragment = this.route.snapshot.fragment;
      if (fragment) {
        this.jump(fragment);
      }
    });
  }

  ngOnInit(): void {
    // Without an answer the guide just leaves the assistant out.
    this.assistant.getStatus().subscribe({
      next: (status) => this.assistantEnabled.set(status.enabled),
      error: () => undefined,
    });
  }

  /**
   * Scrolls to a part and moves the focus to its heading, so the keyboard and a screen reader
   * carry on from there. The address keeps the part, through the link's fragment.
   */
  protected jump(id: string): void {
    const heading = this.document.getElementById(`${id}-title`);
    if (!heading) {
      return;
    }
    const still = this.document.defaultView?.matchMedia?.(
      '(prefers-reduced-motion: reduce)',
    ).matches;
    heading.scrollIntoView({ behavior: still ? 'auto' : 'smooth', block: 'start' });
    heading.focus({ preventScroll: true });
  }
}
