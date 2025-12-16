import { Component, Input, Output, EventEmitter, forwardRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NG_VALUE_ACCESSOR, ControlValueAccessor } from '@angular/forms';

@Component({
  selector: 'ui-search-input',
  standalone: true,
  imports: [CommonModule, FormsModule],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => SearchInputComponent),
    multi: true
  }],
  template: `
    <div class="search-wrapper">
      <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <circle cx="11" cy="11" r="8"/>
        <path d="m21 21-4.35-4.35"/>
      </svg>
      <input 
        type="text" 
        class="search-input"
        [placeholder]="placeholder"
        [(ngModel)]="value"
        (ngModelChange)="onChange($event)"
        (input)="onSearch.emit(value)">
    </div>
  `,
  styles: [`
    .search-wrapper {
      position: relative;
      display: flex;
      align-items: center;
    }

    .search-icon {
      position: absolute;
      left: 10px;
      color: #94a3b8;
      pointer-events: none;
    }

    .search-input {
      width: 100%;
      padding: 6px 10px 6px 32px;
      font-family: var(--font-family);
      font-size: 12px;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      background: white;
      color: #1e293b;
    }

    .search-input::placeholder {
      color: #94a3b8;
    }

    .search-input:focus {
      outline: none;
      border-color: #3b82f6;
    }
  `]
})
export class SearchInputComponent implements ControlValueAccessor {
  @Input() placeholder = 'Rechercher...';
  @Output() onSearch = new EventEmitter<string>();

  value = '';
  onChange: any = () => {};
  onTouched: any = () => {};

  writeValue(val: any) {
    this.value = val || '';
  }

  registerOnChange(fn: any) {
    this.onChange = fn;
  }

  registerOnTouched(fn: any) {
    this.onTouched = fn;
  }
}
