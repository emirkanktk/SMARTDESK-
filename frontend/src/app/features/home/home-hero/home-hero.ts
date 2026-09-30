import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../../../shared/components/icon/icon';
import { HeroPreview } from '../hero-preview/hero-preview';

/**
 * The top of the front page: what SmartDesk does, the way in, and a picture of it. It comes in
 * line by line as the page opens.
 */
@Component({
  selector: 'app-home-hero',
  imports: [RouterLink, HeroPreview, Icon],
  templateUrl: './home-hero.html',
  styleUrl: './home-hero.scss',
})
export class HomeHero {
  /** Ticked under the buttons. */
  protected readonly assurances: readonly string[] = [
    $localize`:@@home.assurance.live:Live calendar`,
    $localize`:@@home.assurance.languages:Turkish and English`,
    $localize`:@@home.assurance.themes:Light and dark mode`,
  ];
}
