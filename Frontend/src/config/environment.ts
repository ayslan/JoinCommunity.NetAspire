// Environment configuration for Aspire service discovery
export const config = {
    // Get Main API URL with fallback
    getMainApiUrl: (): string => {
        // Primary: Use the standard environment variable
        const mainApiUrl = process.env.REACT_APP_MAIN_API_URL;
        if (mainApiUrl) {
            console.log(`Using Main API URL from REACT_APP_MAIN_API_URL: ${mainApiUrl}`);
            return mainApiUrl;
        }

        // Fallback: Try Aspire-style environment variables
        const aspireEnvVars = [
            'REACT_APP_services__main_api__https__0',
            'REACT_APP_services__main_api__http__0',
            'REACT_APP_services__main_api__0',
            'REACT_APP_services__main-api__https__0',
            'REACT_APP_services__main-api__http__0',
            'REACT_APP_services__main-api__0'
        ];

        for (const envVar of aspireEnvVars) {
            const url = process.env[envVar];
            if (url) {
                console.log(`Found Main API URL in ${envVar}: ${url}`);
                return url;
            }
        }

        // Development fallback
        const fallback = 'https://localhost:1111';
        console.log(`Using fallback Main API URL: ${fallback}`);
        
        // Debug info in development
        if (process.env.NODE_ENV === 'development') {
            console.log('All available environment variables:', 
                Object.keys(process.env)
                    .filter(key => key.includes('REACT_APP'))
                    .map(key => `${key}=${process.env[key]}`)
            );
        }
        
        return fallback;
    }
};