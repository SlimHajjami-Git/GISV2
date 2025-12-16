import { Component, Input, Output, EventEmitter, forwardRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NG_VALUE_ACCESSOR, ControlValueAccessor } from '@angular/forms';

@Component({
  selector: 'ui-select',
  standalone: true,
  imports: [CommonModule, FormsModule],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => SelectComponent),
    multi: true
  }],
  template: `
    <div class="select-wrapper">
      <label class="select-label" *ngIf="label">{{ label }}</label>
      <select 
        class="select-input" 
        [class.small]="size === 'sm'"
        [(ngModel)]="value"
        (ngModelChange)="onChange($event)">
        <option value="" *ngIf="placeholder">{{ placeholder }}</option>
        <ng-content></ng-content>
      </select>
    </div>
  `,
  styles: [`
    .select-wrapper {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .select-label {
      font-size: 11px;
      font-weight: 500;
      color: #64748b;
    }

    .select-input {
      width: 100%;
      padding: 6px 10px;
      font-family: var(--font-family);
      font-size: 12px;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      background: white;
      color: #1e293b;
      cursor: pointer;
    }

    .select-input:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .select-input.small {
      padding: 4px 8px;
      font-size: 11px;
    }
  `]
})
export class SelectComponent implements ControlValueAccessor {
  @Input() label = '';
  @Input() placeholder = '';
  @Input() size: 'sm' | 'md' = 'md';

  value: any = '';
  onChange: any = () => {};
  onTouched: any = () => {};

  writeValue(val: any) {
    this.value = val;
  }

  registerOnChange(fn: any) {
    this.onChange = fn;
  }

  registerOnTouched(fn: any) {
    this.onTouched = fn;
  }
}
