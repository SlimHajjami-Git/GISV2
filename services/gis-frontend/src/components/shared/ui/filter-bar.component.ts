import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'ui-filter-bar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="filter-bar" [class.bordered]="bordered">
      <ng-content></ng-content>
    </div>
  `,
  styles: [`
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: white;
    }

    .filter-bar.bordered {
      border-bottom: 1px solid #e2e8f0;
    }
  `]
})
export class FilterBarComponent {
  @Input() bordered = true;
}
