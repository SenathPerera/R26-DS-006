import {environment} from '../../config/environment';

export interface ComponentDHealth {
  status: string;
  model_loaded?: boolean;
}

export const componentDService = {
  async health(): Promise<ComponentDHealth> {
    const response = await fetch(`${environment.componentDBaseUrl}/health`);
    if (!response.ok) throw new Error(`Component D unavailable (${response.status})`);
    return response.json() as Promise<ComponentDHealth>;
  },
};
